using Terminal.Gui;
using Idealista.Helpers;
using Idealista.Models;
using Newtonsoft.Json;

class Program
{
    // ── UI references ────────────────────────────────────────────────────────
    private static TextView?  _logView;
    private static Label?     _statusLabel;
    private static Label?     _countLabel;
    private static Label?     _lastRunLabel;
    private static Label?     _countdownLabel;
    private static Label?     _cycleCountLabel;
    private static Label?     _leadsLabel;
    private static Label?     _emailsLabel;
    private static Label?     _scanningLabel;
    private static ListView?  _locationsList;
    private static Label?     _lastPropTitle;
    private static Label?     _lastPropLocation;
    private static Label?     _lastPropPrice;
    private static Label?     _lastPropVendor;
    private static Label?     _lastPropDesc;
    private static Button?    _startButton;
    private static Button?    _stopButton;

    // ── State ────────────────────────────────────────────────────────────────
    private static bool                      _isRunning  = false;
    private static CancellationTokenSource?  _cts;
    private static int                       _cycleCount      = 0;
    private static int                       _newLeadsSession = 0;
    private static DateTime?                 _nextCycleTime   = null;
    private static System.Threading.Timer?   _countdownTimer  = null;

    // ── Color schemes ────────────────────────────────────────────────────────
    private static ColorScheme _schemeMain   = new();
    private static ColorScheme _schemeFrame  = new();
    private static ColorScheme _schemeAccent = new();
    private static ColorScheme _schemeLog    = new();

    // ─────────────────────────────────────────────────────────────────────────
    static void Main(string[] args)
    {
        Application.Init();

        // Build color schemes (must be after Application.Init)
        _schemeMain = new ColorScheme
        {
            Normal    = new Terminal.Gui.Attribute(Color.White,       Color.Black),
            Focus     = new Terminal.Gui.Attribute(Color.BrightCyan,  Color.Black),
            HotNormal = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
            HotFocus  = new Terminal.Gui.Attribute(Color.BrightYellow,Color.Black),
            Disabled  = new Terminal.Gui.Attribute(Color.Gray,        Color.Black),
        };
        _schemeFrame = new ColorScheme
        {
            Normal    = new Terminal.Gui.Attribute(Color.BrightCyan,  Color.Black),
            Focus     = new Terminal.Gui.Attribute(Color.BrightCyan,  Color.Black),
            HotNormal = new Terminal.Gui.Attribute(Color.BrightCyan,  Color.Black),
            HotFocus  = new Terminal.Gui.Attribute(Color.BrightCyan,  Color.Black),
            Disabled  = new Terminal.Gui.Attribute(Color.Gray,        Color.Black),
        };
        _schemeAccent = new ColorScheme
        {
            Normal    = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
            Focus     = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
            HotNormal = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
            HotFocus  = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
            Disabled  = new Terminal.Gui.Attribute(Color.Gray,        Color.Black),
        };
        _schemeLog = new ColorScheme
        {
            Normal    = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
            Focus     = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
            HotNormal = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
            HotFocus  = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
            Disabled  = new Terminal.Gui.Attribute(Color.Gray,        Color.Black),
        };

        var top = Application.Top;
        top.ColorScheme = _schemeMain;

        var window = new Window("  ◈  InmoAlert  ·  Idealista Property Scraper v1.0  ◈  ")
        {
            X = 0, Y = 0,
            Width = Dim.Fill(), Height = Dim.Fill(),
            ColorScheme = _schemeMain,
        };

        // ── STATUS FRAME ─────────────────────────────────────────────────────
        var statusFrame = new FrameView("  Sistema  ")
        {
            X = 1, Y = 1,
            Width = Dim.Fill() - 1, Height = 5,
            ColorScheme = _schemeFrame,
        };

        // Row 0: status | BD | cycles | leads | emails
        _statusLabel = new Label("🔴 DETENIDO")
        { X = 1, Y = 0, Width = 18, ColorScheme = _schemeMain };

        var sep1a = new Label("│") { X = 20, Y = 0, Width = 1 };

        _countLabel = new Label("📊 BD: 0")
        { X = 22, Y = 0, Width = 14, ColorScheme = _schemeMain };

        var sep1b = new Label("│") { X = 37, Y = 0, Width = 1 };

        _cycleCountLabel = new Label("🔄 Ciclos: 0")
        { X = 39, Y = 0, Width = 16, ColorScheme = _schemeMain };

        var sep1c = new Label("│") { X = 56, Y = 0, Width = 1 };

        _leadsLabel = new Label("🏠 Leads: 0")
        { X = 58, Y = 0, Width = 16, ColorScheme = _schemeAccent };

        var sep1d = new Label("│") { X = 75, Y = 0, Width = 1 };

        _emailsLabel = new Label("✉  Emails: 0")
        { X = 77, Y = 0, Width = 18, ColorScheme = _schemeAccent };

        // Row 1: last run | countdown | scanning
        _lastRunLabel = new Label("⏰ Último ciclo: Nunca")
        { X = 1, Y = 1, Width = 32, ColorScheme = _schemeMain };

        var sep2a = new Label("│") { X = 34, Y = 1, Width = 1 };

        _countdownLabel = new Label("⏳ Próximo en: --:--")
        { X = 36, Y = 1, Width = 26, ColorScheme = _schemeMain };

        var sep2b = new Label("│") { X = 63, Y = 1, Width = 1 };

        _scanningLabel = new Label("📍 En espera")
        { X = 65, Y = 1, Width = Dim.Fill() - 2, ColorScheme = _schemeMain };

        statusFrame.Add(
            _statusLabel, sep1a, _countLabel, sep1b, _cycleCountLabel,
            sep1c, _leadsLabel, sep1d, _emailsLabel,
            _lastRunLabel, sep2a, _countdownLabel, sep2b, _scanningLabel
        );

        // ── LAST LEAD FRAME ───────────────────────────────────────────────────
        var lastLeadFrame = new FrameView("  Último Lead Encontrado  ")
        {
            X = 1, Y = 7,
            Width = Dim.Fill() - 1, Height = 7,
            ColorScheme = _schemeFrame,
        };

        _lastPropTitle = new Label("─  Sin datos aún  ─")
        { X = 1, Y = 0, Width = Dim.Fill() - 2, ColorScheme = _schemeAccent };

        _lastPropLocation = new Label("")
        { X = 1, Y = 1, Width = Dim.Fill() - 2, ColorScheme = _schemeMain };

        _lastPropPrice = new Label("")
        { X = 1, Y = 2, Width = 40, ColorScheme = _schemeMain };

        _lastPropVendor = new Label("")
        { X = 1, Y = 3, Width = Dim.Fill() - 2, ColorScheme = _schemeMain };

        _lastPropDesc = new Label("")
        { X = 1, Y = 4, Width = Dim.Fill() - 2, ColorScheme = _schemeMain };

        lastLeadFrame.Add(
            _lastPropTitle, _lastPropLocation,
            _lastPropPrice, _lastPropVendor, _lastPropDesc
        );

        // ── LOCATIONS FRAME ──────────────────────────────────────────────────
        var locationsFrame = new FrameView("  Ubicaciones  ")
        {
            X = 1, Y = 15,
            Width = 28, Height = Dim.Fill() - 18,
            ColorScheme = _schemeFrame,
        };

        _locationsList = new ListView()
        {
            X = 0, Y = 0,
            Width = Dim.Fill(), Height = Dim.Fill(),
            ColorScheme = _schemeMain,
        };
        locationsFrame.Add(_locationsList);

        // ── LOG FRAME ────────────────────────────────────────────────────────
        var logFrame = new FrameView("  Log de Actividad  ")
        {
            X = 30, Y = 15,
            Width = Dim.Fill() - 1, Height = Dim.Fill() - 18,
            ColorScheme = _schemeFrame,
        };

        _logView = new TextView()
        {
            X = 0, Y = 0,
            Width = Dim.Fill(), Height = Dim.Fill(),
            ReadOnly = true, WordWrap = true,
            ColorScheme = _schemeLog,
        };
        logFrame.Add(_logView);

        // ── BUTTONS ──────────────────────────────────────────────────────────
        _startButton = new Button("▶  Iniciar Scraping")
        {
            X = 2,
            Y = Pos.Bottom(window) - 3,
            ColorScheme = _schemeAccent,
        };

        _stopButton = new Button("⏸  Detener")
        {
            X = Pos.Right(_startButton) + 2,
            Y = Pos.Bottom(window) - 3,
            Enabled = false,
        };

        var clearLogButton = new Button("🗑  Limpiar Log")
        {
            X = Pos.Right(_stopButton) + 2,
            Y = Pos.Bottom(window) - 3,
        };

        var exitButton = new Button("✖  Salir")
        {
            X = Pos.Right(clearLogButton) + 2,
            Y = Pos.Bottom(window) - 3,
        };

        // ── BUTTON EVENTS ────────────────────────────────────────────────────
        _startButton.Clicked += async () =>
        {
            if (!_isRunning)
            {
                _isRunning = true;
                _startButton.Enabled = false;
                _stopButton.Enabled = true;
                _cts = new CancellationTokenSource();
                _ = Task.Run(async () => await StartScrapingAsync(_cts.Token));
            }
        };

        _stopButton.Clicked += () =>
        {
            _isRunning = false;
            _cts?.Cancel();
            _startButton!.Enabled = true;
            _stopButton.Enabled = false;
            _nextCycleTime = null;
            UpdateStatus("🔴 DETENIDO");
            UpdateScanning("📍 En espera");
            Log("⏹ Scraping detenido manualmente");
        };

        clearLogButton.Clicked += () =>
        {
            Application.MainLoop.Invoke(() =>
            {
                if (_logView != null) _logView.Text = "";
            });
            Log("🗑 Log limpiado");
        };

        exitButton.Clicked += () =>
        {
            if (_isRunning)
            {
                var result = MessageBox.Query("Confirmar salida",
                    "El scraping está en ejecución.\n¿Desea salir?", "Sí", "No");
                if (result == 0)
                {
                    _cts?.Cancel();
                    _countdownTimer?.Dispose();
                    Application.RequestStop();
                }
            }
            else
            {
                _countdownTimer?.Dispose();
                Application.RequestStop();
            }
        };

        window.Add(
            statusFrame, lastLeadFrame,
            locationsFrame, logFrame,
            _startButton, _stopButton, clearLogButton, exitButton
        );
        top.Add(window);

        // Register callback for new properties
        ScraperHelper.OnNewPropertyFound = OnNewPropertyFound;

        // Countdown timer – fires every second
        _countdownTimer = new System.Threading.Timer(
            _ => UpdateCountdown(), null, 0, 1000);

        InitializeSystem();

        Application.Run();
        Application.Shutdown();

        _countdownTimer?.Dispose();
        ScraperHelper.DisposeAsync().GetAwaiter().GetResult();
    }

    // ── Callback ─────────────────────────────────────────────────────────────
    static void OnNewPropertyFound(Property property)
    {
        _newLeadsSession++;

        Application.MainLoop.Invoke(() =>
        {
            if (_leadsLabel  != null) _leadsLabel.Text  = $"🏠 Leads: {_newLeadsSession}";
            if (_emailsLabel != null) _emailsLabel.Text = $"✉  Emails: {EmailHelper.EmailsSentSession}";

            if (_lastPropTitle    != null)
                _lastPropTitle.Text    = $"🏠  {property.Title}";
            if (_lastPropLocation != null)
                _lastPropLocation.Text = $"📍  {property.Location}, {property.Province}";
            if (_lastPropPrice    != null)
                _lastPropPrice.Text    = $"💰  {property.Price:N0} {property.Currency}";
            if (_lastPropVendor   != null)
            {
                var type = property.IsBank ? "Banco" : property.IsProfessional ? "Profesional" : "Particular";
                _lastPropVendor.Text = $"👤  {type}   |   🆔  {property.Id}   |   🔗  {property.Url}";
            }
            if (_lastPropDesc != null)
            {
                var desc = property.Description.Length > 100
                    ? property.Description[..97] + "..."
                    : property.Description;
                _lastPropDesc.Text = $"📝  {desc}";
            }
        });

        Log($"✉ Email enviado → {property.Title} · {property.Price:N0}€");
    }

    // ── Countdown update (called from Timer thread) ───────────────────────────
    static void UpdateCountdown()
    {
        if (_nextCycleTime.HasValue && _isRunning)
        {
            var remaining = _nextCycleTime.Value - DateTime.Now;
            var text = remaining.TotalSeconds > 0
                ? $"⏳ Próximo en: {(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}"
                : "⏳ Iniciando ciclo...";
            Application.MainLoop?.Invoke(() =>
            {
                if (_countdownLabel != null) _countdownLabel.Text = text;
            });
        }
        else if (!_isRunning)
        {
            Application.MainLoop?.Invoke(() =>
            {
                if (_countdownLabel != null) _countdownLabel.Text = "⏳ Próximo en: --:--";
            });
        }
    }

    // ── System init ──────────────────────────────────────────────────────────
    static void InitializeSystem()
    {
        try
        {
            Log("🚀 Iniciando sistema InmoAlert...");
            DatabaseHelper.InitializeDatabase();
            Log("✓ Base de datos inicializada");

            string jsonPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Resources", "locations.json");
            string jsonContent = File.ReadAllText(jsonPath);
            var locationData = JsonConvert.DeserializeObject<LocationData>(jsonContent);

            if (locationData?.Locations != null && locationData.Locations.Count > 0)
            {
                var names = locationData.Locations.Select(l => l.Name).ToList();
                Application.MainLoop.Invoke(() =>
                {
                    if (_locationsList != null) _locationsList.SetSource(names);
                });
                Log($"✓ {locationData.Locations.Count} ubicación(es) cargada(s)");
            }
            else
            {
                Log("⚠ No se encontraron ubicaciones en locations.json");
            }

            UpdateCount();
            Log("✓ Sistema listo  –  Presione ▶ Iniciar Scraping para comenzar");
        }
        catch (Exception ex)
        {
            Log($"❌ Error en inicialización: {ex.Message}");
        }
    }

    // ── Main scraping loop ───────────────────────────────────────────────────
    static async Task StartScrapingAsync(CancellationToken cancellationToken)
    {
        try
        {
            UpdateStatus("🟢 EJECUTANDO");
            Log("▶ Iniciando ciclo de scraping...");

            string jsonPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Resources", "locations.json");
            string jsonContent = File.ReadAllText(jsonPath);
            var locationData = JsonConvert.DeserializeObject<LocationData>(jsonContent);

            if (locationData?.Locations == null || locationData.Locations.Count == 0)
            {
                Log("❌ No hay ubicaciones configuradas");
                return;
            }

            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                _cycleCount++;
                Application.MainLoop.Invoke(() =>
                {
                    if (_cycleCountLabel != null)
                        _cycleCountLabel.Text = $"🔄 Ciclos: {_cycleCount}";
                });

                Log($"\n{'═',60}");
                Log($"🔄 Ciclo #{_cycleCount}  —  {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
                Log($"{'═',60}\n");

                foreach (var location in locationData.Locations)
                {
                    if (!_isRunning || cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        HighlightLocation(locationData.Locations.IndexOf(location));
                        UpdateScanning($"📍 Escaneando: {location.Name}");

                        Log($"🔍 Procesando: {location.Name}");
                        var startTime = DateTime.Now;

                        var properties = await ScraperHelper.ScrapeProperties(location);

                        var duration = (DateTime.Now - startTime).TotalMinutes;
                        Log($"⏱ Completado en {duration:F2} min  –  {properties.Count} propiedades");
                        UpdateCount();

                        if (location != locationData.Locations.Last() && _isRunning)
                        {
                            Log("⏳ Esperando 30 s antes de la siguiente ubicación...\n");
                            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Log("⏹ Operación cancelada");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log($"❌ Error en {location.Name}: {ex.Message}\n");
                    }
                }

                await ScraperHelper.DisposeAsync();
                UpdateLastRun();
                UpdateScanning("📍 En espera");

                if (_isRunning && !cancellationToken.IsCancellationRequested)
                {
                    _nextCycleTime = DateTime.Now.AddMinutes(5);
                    Log("\n✓ Ciclo completado  –  Próximo ciclo en 5 minutos...\n");
                    await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                    _nextCycleTime = null;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log("⏹ Scraping cancelado");
        }
        catch (Exception ex)
        {
            Log($"❌ Error crítico: {ex.Message}");
        }
        finally
        {
            _isRunning = false;
            _nextCycleTime = null;
            UpdateStatus("🔴 DETENIDO");
            UpdateScanning("📍 En espera");

            Application.MainLoop.Invoke(() =>
            {
                if (_startButton != null) _startButton.Enabled = true;
                if (_stopButton  != null) _stopButton.Enabled  = false;
            });
        }
    }

    // ── UI helpers ───────────────────────────────────────────────────────────
    static void HighlightLocation(int index)
    {
        Application.MainLoop.Invoke(() =>
        {
            if (_locationsList != null) _locationsList.SelectedItem = index;
        });
    }

    static void UpdateStatus(string status)
    {
        Application.MainLoop.Invoke(() =>
        {
            if (_statusLabel != null) _statusLabel.Text = status;
        });
    }

    static void UpdateScanning(string text)
    {
        Application.MainLoop.Invoke(() =>
        {
            if (_scanningLabel != null) _scanningLabel.Text = text;
        });
    }

    static void UpdateCount()
    {
        Application.MainLoop.Invoke(() =>
        {
            if (_countLabel != null)
            {
                try
                {
                    int count = DatabaseHelper.GetPropertyCount();
                    _countLabel.Text = $"📊 BD: {count:N0}";
                }
                catch (Exception ex)
                {
                    _countLabel.Text = $"📊 BD: error ({ex.Message})";
                }
            }
        });
    }

    static void UpdateLastRun()
    {
        Application.MainLoop.Invoke(() =>
        {
            if (_lastRunLabel != null)
                _lastRunLabel.Text = $"⏰ Último ciclo: {DateTime.Now:HH:mm:ss}";
        });
    }

    static void Log(string message)
    {
        Application.MainLoop.Invoke(() =>
        {
            if (_logView != null)
            {
                var ts = DateTime.Now.ToString("HH:mm:ss");
                _logView.Text += $"[{ts}] {message}\n";
                _logView.MoveEnd();
            }
        });
    }
}

public class LocationData
{
    public List<Location> Locations { get; set; } = new();
}
