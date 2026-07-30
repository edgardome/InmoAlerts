using InmoAlerts.Configuration;
using InmoAlerts.Helpers;
using InmoAlerts.Models;
using Microsoft.Extensions.Options;

namespace InmoAlerts.Services
{
    public class ScraperBackgroundService : BackgroundService
    {
        private readonly ScraperState _state;
        private readonly LogService _log;
        private readonly IOptionsMonitor<ScraperSettings> _settingsMonitor;
        private CancellationTokenSource? _scrapingCts;

        private ScraperSettings Settings => _settingsMonitor.CurrentValue;

        public ScraperBackgroundService(ScraperState state, LogService log, IOptionsMonitor<ScraperSettings> settings)
        {
            _state = state;
            _log = log;
            _settingsMonitor = settings;

            ScraperHelper.OnNewPropertyFound = OnNewPropertyFound;
            ScraperHelper.OnPriceChanged = OnPriceChanged;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.Log("🚀 InmoAlerts iniciado. Presiona Iniciar para comenzar.");

            Helpers.DatabaseHelper.InitializeDatabase();
            _state.PropertyCount = Helpers.DatabaseHelper.GetPropertyCount();
            _log.Log("✓ Base de datos inicializada");

            // Wait until start is requested or app stops
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_state.IsRunning && _scrapingCts == null)
                {
                    _scrapingCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    _ = RunScrapingLoopAsync(_scrapingCts.Token);
                }
                await Task.Delay(500, stoppingToken).ContinueWith(_ => { });
            }
        }

        public void Start()
        {
            if (_state.IsRunning) return;
            _state.IsRunning = true;
            _state.NewLeadsSession = 0;
            _state.EmailsSentSession = 0;
        }

        public async Task StopAsync()
        {
            _state.IsRunning = false;
            _state.NextCycleTime = null;
            _state.CurrentLocation = "En espera";

            if (_scrapingCts != null)
            {
                await _scrapingCts.CancelAsync();
                _scrapingCts = null;
            }

            await ScraperHelper.DisposeAsync();
            _log.Log("⏹ Scraping detenido");
        }

        // ── Orchestrator: one Task per zone, all running in parallel ─────────────

        private async Task RunScrapingLoopAsync(CancellationToken ct)
        {
            try
            {
                _log.Log("▶ Iniciando ciclos paralelos de scraping por zona...");

                var locations = Helpers.DatabaseHelper.GetAllLocations();

                if (locations.Count == 0)
                {
                    _log.Log("❌ No hay ubicaciones configuradas en la base de datos");
                    _state.IsRunning = false;
                    _scrapingCts = null;
                    return;
                }

                _log.Log($"📍 {locations.Count} zona(s) configurada(s). Lanzando ciclos paralelos...");

                // Launch one loop per zone with a staggered offset so they don't
                // all hit Idealista at exactly the same second.
                const int offsetSeconds = 45;
                var zoneTasks = locations
                    .Select((loc, idx) => RunLocationLoopAsync(loc, idx * offsetSeconds, ct))
                    .ToList();

                await Task.WhenAll(zoneTasks);
            }
            catch (OperationCanceledException)
            {
                _log.Log("⏹ Scraping cancelado");
            }
            catch (Exception ex)
            {
                _log.Log($"❌ Error crítico en orquestador: {ex.Message}");
            }
            finally
            {
                _state.IsRunning = false;
                _state.NextCycleTime = null;
                _state.CurrentLocation = "En espera";
                _scrapingCts = null;
            }
        }

        // ── Per-zone loop ─────────────────────────────────────────────────────────

        private async Task RunLocationLoopAsync(Location location, int initialOffsetSeconds, CancellationToken ct)
        {
            // Staggered start: wait offset so zones don't all fire at T+0
            if (initialOffsetSeconds > 0 && !ct.IsCancellationRequested)
            {
                _log.Log($"⏳ Zona [{location.Name}] inicia en {initialOffsetSeconds}s...");
                await Task.Delay(TimeSpan.FromSeconds(initialOffsetSeconds), ct).ContinueWith(_ => { });
            }

            int cycleCount = 0;

            while (_state.IsRunning && !ct.IsCancellationRequested)
            {
                // Reload fresh location data each cycle (interval or Telegram chatId may have changed)
                var freshLocations = Helpers.DatabaseHelper.GetAllLocations();
                var fresh = freshLocations.FirstOrDefault(l => l.Id == location.Id);
                if (fresh == null) break; // zone was deleted — stop this loop
                location = fresh;

                cycleCount++;
                int intervalMin = location.ScanIntervalMinutes > 0 ? location.ScanIntervalMinutes : 2;

                _log.Log($"\n[{location.Name}] 🔄 Ciclo #{cycleCount}  —  {DateTime.Now:HH:mm:ss}");

                // ── Venta ──────────────────────────────────────────────────────────
                if (!Settings.AlertVenta)
                {
                    _log.Log($"[{location.Name}] ⏭ Venta omitida (desactivada globalmente)");
                }
                else
                {
                    try
                    {
                        _state.CurrentLocation = $"{location.Name} (Venta)";
                        _log.Log($"[{location.Name}] 🔍 Venta...");
                        var sw = System.Diagnostics.Stopwatch.StartNew();

                        var props = await ScraperHelper.ScrapeProperties(location, "Venta");

                        sw.Stop();
                        _log.Log($"[{location.Name}] ⏱ Venta: {sw.Elapsed.TotalSeconds:F1}s — {props.Count} prop(s)");
                        _state.PropertyCount = Helpers.DatabaseHelper.GetPropertyCount();
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { _log.Log($"[{location.Name}] ❌ Error venta: {ex.Message}"); }
                }

                if (!_state.IsRunning || ct.IsCancellationRequested) break;

                // ── Alquiler ──────────────────────────────────────────────────────
                string? rentUrl = location.RentUrl;
                if (string.IsNullOrWhiteSpace(rentUrl))
                    rentUrl = DeriveRentUrl(location.Url);

                if (!Settings.AlertAlquiler)
                {
                    _log.Log($"[{location.Name}] ⏭ Alquiler omitido (desactivado globalmente)");
                }
                else if (!string.IsNullOrWhiteSpace(rentUrl))
                {
                    try
                    {
                        // Small delay between venta and alquiler for the same zone
                        await Task.Delay(TimeSpan.FromSeconds(20), ct);

                        var rentLocation = new Location
                        {
                            Id                  = location.Id,
                            Name                = location.Name,
                            Url                 = rentUrl,
                            SortByRecent        = location.SortByRecent,
                            ScanIntervalMinutes = location.ScanIntervalMinutes,
                            TelegramChatId      = location.TelegramChatId
                        };

                        _state.CurrentLocation = $"{location.Name} (Alquiler)";
                        _log.Log($"[{location.Name}] 🔍 Alquiler...");
                        var sw = System.Diagnostics.Stopwatch.StartNew();

                        var rentProps = await ScraperHelper.ScrapeProperties(rentLocation, "Alquiler");

                        sw.Stop();
                        _log.Log($"[{location.Name}] ⏱ Alquiler: {sw.Elapsed.TotalSeconds:F1}s — {rentProps.Count} prop(s)");
                        _state.PropertyCount = Helpers.DatabaseHelper.GetPropertyCount();
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { _log.Log($"[{location.Name}] ❌ Error alquiler: {ex.Message}"); }
                }

                // ── Wait until next cycle ─────────────────────────────────────────
                if (_state.IsRunning && !ct.IsCancellationRequested)
                {
                    _log.Log($"[{location.Name}] ✓ Ciclo #{cycleCount} completado — próximo en {intervalMin} min");
                    await Task.Delay(TimeSpan.FromMinutes(intervalMin), ct).ContinueWith(_ => { });
                }
            }

            _log.Log($"[{location.Name}] ⏹ Loop de zona detenido.");
        }

        private void OnNewPropertyFound(Property property)
        {
            _state.NewLeadsSession++;
            _state.EmailsSentSession = EmailHelper.EmailsSentSession;
            _state.LastProperty = property;
            _state.PropertyCount = Helpers.DatabaseHelper.GetPropertyCount();
            string tag = property.ListingType == "Alquiler" ? "🏠 Alquiler" : "🏷 Venta";
            _log.Log($"✉ Nuevo lead [{tag}] → {property.Title} · {property.Price:N0}€ ({property.Location})");
        }

        private void OnPriceChanged(Property property, int oldPrice)
        {
            _state.EmailsSentSession = EmailHelper.EmailsSentSession;
            _state.LastProperty = property;
            string tag = property.ListingType == "Alquiler" ? "🏠 Alquiler" : "🏷 Venta";
            string direction = property.Price < oldPrice ? "⬇ bajada" : "⬆ subida";
            _log.Log($"💰 Cambio de precio [{tag}] {direction}: {property.Title} · {oldPrice:N0}€ → {property.Price:N0}€ ({property.Location})");
        }

        /// <summary>
        /// Derives an Idealista rental URL from a sale URL.
        /// e.g. /venta-viviendas/... → /alquiler-viviendas/...
        /// Returns null if the pattern is not recognised.
        /// </summary>
        private static string? DeriveRentUrl(string saleUrl)
        {
            var replacements = new[]
            {
                ("venta-viviendas",     "alquiler-viviendas"),
                ("venta-locales",       "alquiler-locales"),
                ("venta-oficinas",      "alquiler-oficinas"),
                ("venta-garajes",       "alquiler-garajes"),
                ("venta-trasteros",     "alquiler-trasteros"),
                ("casas-en-venta",      "casas-en-alquiler"),
                ("pisos-en-venta",      "pisos-en-alquiler"),
            };

            foreach (var (saleSegment, rentSegment) in replacements)
            {
                if (saleUrl.Contains(saleSegment, StringComparison.OrdinalIgnoreCase))
                    return saleUrl.Replace(saleSegment, rentSegment, StringComparison.OrdinalIgnoreCase);
            }

            return null;
        }
    }
}
