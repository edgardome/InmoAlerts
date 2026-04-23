using Idealista.Configuration;
using Idealista.Helpers;
using Idealista.Models;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Idealista.Services
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
            _log.Log("🚀 NexumLabs iniciado. Presiona Iniciar para comenzar.");

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

        private async Task RunScrapingLoopAsync(CancellationToken ct)
        {
            try
            {
                _log.Log("▶ Iniciando ciclo de scraping...");

                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "locations.json");
                string jsonContent = await File.ReadAllTextAsync(jsonPath, ct);
                var locationData = JsonConvert.DeserializeObject<LocationData>(jsonContent);

                if (locationData?.Locations == null || locationData.Locations.Count == 0)
                {
                    _log.Log("❌ No hay ubicaciones configuradas en locations.json");
                    _state.IsRunning = false;
                    _scrapingCts = null;
                    return;
                }

                while (_state.IsRunning && !ct.IsCancellationRequested)
                {
                    _state.CycleCount++;
                    _log.Log($"\n{"═",60}");
                    _log.Log($"🔄 Ciclo #{_state.CycleCount}  —  {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
                    _log.Log($"{"═",60}\n");

                    // Reload locations each cycle so changes take effect
                    jsonContent = await File.ReadAllTextAsync(jsonPath, ct);
                    locationData = JsonConvert.DeserializeObject<LocationData>(jsonContent);
                    if (locationData?.Locations == null) break;

                    foreach (var location in locationData.Locations)
                    {
                        if (!_state.IsRunning || ct.IsCancellationRequested) break;

                        // ── Venta ──────────────────────────────────────────────
                        if (!Settings.AlertVenta)
                        {
                            _log.Log($"⏭ Venta omitida (alertas de venta desactivadas): {location.Name}");
                        }
                        else
                        try
                        {
                            _state.CurrentLocation = $"{location.Name} (Venta)";
                            _log.Log($"🔍 Procesando venta: {location.Name}");
                            var startTime = DateTime.Now;

                            var properties = await ScraperHelper.ScrapeProperties(location, "Venta");

                            var duration = (DateTime.Now - startTime).TotalMinutes;
                            _log.Log($"⏱ Venta completado en {duration:F2} min  –  {properties.Count} propiedades");
                            _state.PropertyCount = Helpers.DatabaseHelper.GetPropertyCount();
                        }
                        catch (OperationCanceledException) { _log.Log("⏹ Operación cancelada"); break; }
                        catch (Exception ex) { _log.Log($"❌ Error venta {location.Name}: {ex.Message}\n"); }

                        // ── Alquiler (RentUrl explícita o auto-derivada del patrón de Idealista) ──
                        string? rentUrl = location.RentUrl;
                        if (string.IsNullOrWhiteSpace(rentUrl))
                            rentUrl = DeriveRentUrl(location.Url);

                        if (!Settings.AlertAlquiler)
                        {
                            _log.Log($"⏭ Alquiler omitido (alertas de alquiler desactivadas): {location.Name}");
                        }
                        else if (!string.IsNullOrWhiteSpace(rentUrl) && _state.IsRunning && !ct.IsCancellationRequested)
                        {
                            try
                            {
                                _log.Log("⏳ Esperando 30 s antes de alquiler...\n");
                                await Task.Delay(TimeSpan.FromSeconds(30), ct);

                                var rentLocation = new Models.Location
                                {
                                    Name = location.Name,
                                    Url = rentUrl
                                };

                                _state.CurrentLocation = $"{location.Name} (Alquiler)";
                                _log.Log($"🔍 Procesando alquiler: {location.Name}  →  {rentUrl}");
                                var startTime = DateTime.Now;

                                var rentProperties = await ScraperHelper.ScrapeProperties(rentLocation, "Alquiler");

                                var duration = (DateTime.Now - startTime).TotalMinutes;
                                _log.Log($"⏱ Alquiler completado en {duration:F2} min  –  {rentProperties.Count} propiedades");
                                _state.PropertyCount = Helpers.DatabaseHelper.GetPropertyCount();
                            }
                            catch (OperationCanceledException) { _log.Log("⏹ Operación cancelada"); break; }
                            catch (Exception ex) { _log.Log($"❌ Error alquiler {location.Name}: {ex.Message}\n"); }
                        }

                        // Delay between locations
                        if (location != locationData.Locations.Last() && _state.IsRunning && !ct.IsCancellationRequested)
                        {
                            _log.Log("⏳ Esperando 30 s antes de la siguiente ubicación...\n");
                            await Task.Delay(TimeSpan.FromSeconds(30), ct);
                        }
                    }

                    await ScraperHelper.DisposeAsync();
                    _state.LastRunTime = DateTime.Now;
                    _state.CurrentLocation = "En espera";

                    if (_state.IsRunning && !ct.IsCancellationRequested)
                    {
                        int minutes = Settings.MinutesBetweenCycles > 0 ? Settings.MinutesBetweenCycles : 5;
                        _state.NextCycleTime = DateTime.Now.AddMinutes(minutes);
                        _log.Log($"\n✓ Ciclo completado  –  Próximo ciclo en {minutes} minutos...\n");
                        await Task.Delay(TimeSpan.FromMinutes(minutes), ct);
                        _state.NextCycleTime = null;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _log.Log("⏹ Scraping cancelado");
            }
            catch (Exception ex)
            {
                _log.Log($"❌ Error crítico: {ex.Message}");
            }
            finally
            {
                _state.IsRunning = false;
                _state.NextCycleTime = null;
                _state.CurrentLocation = "En espera";
                _scrapingCts = null;
            }
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
            // Standard Idealista sale → rent URL mapping
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

            return null; // URL pattern not recognised — skip alquiler for this location
        }
    }

    public class LocationData
    {
        public List<Location> Locations { get; set; } = new();
    }
}
