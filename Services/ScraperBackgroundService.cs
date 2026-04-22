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
        private readonly ScraperSettings _settings;
        private CancellationTokenSource? _scrapingCts;

        public ScraperBackgroundService(ScraperState state, LogService log, IOptions<ScraperSettings> settings)
        {
            _state = state;
            _log = log;
            _settings = settings.Value;

            ScraperHelper.OnNewPropertyFound = OnNewPropertyFound;
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

                        try
                        {
                            _state.CurrentLocation = location.Name;
                            _log.Log($"🔍 Procesando: {location.Name}");
                            var startTime = DateTime.Now;

                            var properties = await ScraperHelper.ScrapeProperties(location);

                            var duration = (DateTime.Now - startTime).TotalMinutes;
                            _log.Log($"⏱ Completado en {duration:F2} min  –  {properties.Count} propiedades");
                            _state.PropertyCount = Helpers.DatabaseHelper.GetPropertyCount();

                            if (location != locationData.Locations.Last() && _state.IsRunning)
                            {
                                _log.Log("⏳ Esperando 30 s antes de la siguiente ubicación...\n");
                                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            _log.Log("⏹ Operación cancelada");
                            break;
                        }
                        catch (Exception ex)
                        {
                            _log.Log($"❌ Error en {location.Name}: {ex.Message}\n");
                        }
                    }

                    await ScraperHelper.DisposeAsync();
                    _state.LastRunTime = DateTime.Now;
                    _state.CurrentLocation = "En espera";

                    if (_state.IsRunning && !ct.IsCancellationRequested)
                    {
                        int minutes = _settings.MinutesBetweenCycles > 0 ? _settings.MinutesBetweenCycles : 5;
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
            _log.Log($"✉ Nuevo lead → {property.Title} · {property.Price:N0}€ ({property.Location})");
        }
    }

    public class LocationData
    {
        public List<Location> Locations { get; set; } = new();
    }
}
