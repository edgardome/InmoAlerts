using Terminal.Gui;
using Idealista.Helpers;
using Idealista.Models;
using Newtonsoft.Json;

class Program
{
    private static TextView? _logView;
    private static Label? _statusLabel;
    private static Label? _countLabel;
    private static Label? _lastRunLabel;
    private static ListView? _locationsList;
    private static bool _isRunning = false;
    private static CancellationTokenSource? _cancellationTokenSource;

    static void Main(string[] args)
    {
        Application.Init();

        var top = Application.Top;

        // Ventana principal
        var window = new Window("Idealista Property Scraper - v1.0")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        // Panel de estado
        var statusFrame = new FrameView("Estado del Sistema")
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 1,
            Height = 6
        };

        _statusLabel = new Label("🔴 Estado: Detenido")
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill() - 2
        };

        _countLabel = new Label("📊 Propiedades en BD: Cargando...")
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 2
        };

        _lastRunLabel = new Label("⏰ Último ciclo: Nunca")
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill() - 2
        };

        statusFrame.Add(_statusLabel, _countLabel, _lastRunLabel);

        // Panel de ubicaciones
        var locationsFrame = new FrameView("Ubicaciones Configuradas")
        {
            X = 1,
            Y = 7,
            Width = 40,
            Height = Dim.Fill() - 11
        };

        _locationsList = new ListView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        locationsFrame.Add(_locationsList);

        // Panel de log
        var logFrame = new FrameView("Log de Actividad")
        {
            X = 42,
            Y = 7,
            Width = Dim.Fill() - 1,
            Height = Dim.Fill() - 11
        };

        _logView = new TextView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = true
        };

        logFrame.Add(_logView);

        // Botones
        var startButton = new Button("▶ Iniciar Scraping")
        {
            X = 1,
            Y = Pos.Bottom(window) - 3
        };

        var stopButton = new Button("⏸ Detener")
        {
            X = Pos.Right(startButton) + 2,
            Y = Pos.Bottom(window) - 3,
            Enabled = false
        };

        var clearLogButton = new Button("🗑 Limpiar Log")
        {
            X = Pos.Right(stopButton) + 2,
            Y = Pos.Bottom(window) - 3
        };

        var exitButton = new Button("❌ Salir")
        {
            X = Pos.Right(clearLogButton) + 2,
            Y = Pos.Bottom(window) - 3
        };

        // Eventos de botones
        startButton.Clicked += async () =>
        {
            if (!_isRunning)
            {
                _isRunning = true;
                startButton.Enabled = false;
                stopButton.Enabled = true;
                _cancellationTokenSource = new CancellationTokenSource();
                
                _ = Task.Run(async () => await StartScrapingAsync(_cancellationTokenSource.Token));
            }
        };

        stopButton.Clicked += () =>
        {
            _isRunning = false;
            _cancellationTokenSource?.Cancel();
            startButton.Enabled = true;
            stopButton.Enabled = false;
            UpdateStatus("🔴 Detenido por usuario");
            Log("⏹ Scraping detenido manualmente");
        };

        clearLogButton.Clicked += () =>
        {
            Application.MainLoop.Invoke(() =>
            {
                if (_logView != null)
                    _logView.Text = "";
            });
            Log("🗑 Log limpiado");
        };

        exitButton.Clicked += () =>
        {
            if (_isRunning)
            {
                var result = MessageBox.Query("Confirmar salida", 
                    "El scraping está en ejecución. ¿Desea salir?", "Sí", "No");
                if (result == 0)
                {
                    _cancellationTokenSource?.Cancel();
                    Application.RequestStop();
                }
            }
            else
            {
                Application.RequestStop();
            }
        };

        window.Add(statusFrame, locationsFrame, logFrame, startButton, stopButton, clearLogButton, exitButton);
        top.Add(window);

        // Inicializar sistema
        InitializeSystem();

        Application.Run();
        Application.Shutdown();
        
        // Cleanup
        ScraperHelper.DisposeAsync().GetAwaiter().GetResult();
    }

    static void InitializeSystem()
    {
        try
        {
            Log("🚀 Iniciando sistema...");
            
            // Inicializar base de datos
            DatabaseHelper.InitializeDatabase();
            Log("✓ Base de datos inicializada");
            
            // Cargar ubicaciones
            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "locations.json");
            string jsonContent = File.ReadAllText(jsonPath);
            var locationData = JsonConvert.DeserializeObject<LocationData>(jsonContent);

            if (locationData?.Locations != null && locationData.Locations.Count > 0)
            {
                var locationNames = locationData.Locations.Select(l => l.Name).ToList();
                
                Application.MainLoop.Invoke(() =>
                {
                    if (_locationsList != null)
                    {
                        _locationsList.SetSource(locationNames);
                    }
                });

                Log($"✓ Cargadas {locationData.Locations.Count} ubicación(es)");
            }
            else
            {
                Log("⚠ No se encontraron ubicaciones en locations.json");
            }

            // Actualizar contador
            UpdateCount();
            
            Log("✓ Sistema listo");
            Log("ℹ Presione 'Iniciar Scraping' para comenzar");
        }
        catch (Exception ex)
        {
            Log($"❌ Error en inicialización: {ex.Message}");
        }
    }

    static async Task StartScrapingAsync(CancellationToken cancellationToken)
    {
        try
        {
            UpdateStatus("🟢 Ejecutando...");
            Log("▶ Iniciando ciclo de scraping...");

            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "locations.json");
            string jsonContent = File.ReadAllText(jsonPath);
            var locationData = JsonConvert.DeserializeObject<LocationData>(jsonContent);

            if (locationData?.Locations == null || locationData.Locations.Count == 0)
            {
                Log("❌ No hay ubicaciones configuradas");
                return;
            }

            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                Log($"\n{'═',50}");
                Log($"🔄 Nuevo ciclo - {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
                Log($"{'═',50}\n");

                foreach (var location in locationData.Locations)
                {
                    if (!_isRunning || cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        // Highlight ubicación actual
                        HighlightLocation(locationData.Locations.IndexOf(location));

                        Log($"🔍 Procesando: {location.Name}");
                        var startTime = DateTime.Now;

                        var properties = await ScraperHelper.ScrapeProperties(location);

                        var duration = (DateTime.Now - startTime).TotalMinutes;
                        Log($"⏱ Completado en {duration:F2} minutos");
                        Log($"📦 Propiedades encontradas: {properties.Count}");
                        
                        UpdateCount();

                        if (location != locationData.Locations.Last() && _isRunning)
                        {
                            Log("⏳ Esperando 30 segundos antes de la siguiente ubicación...\n");
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

                if (_isRunning && !cancellationToken.IsCancellationRequested)
                {
                    Log("\n✓ Ciclo completado");
                    Log("⏰ Próximo ciclo en 5 minutos...\n");
                    await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
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
            UpdateStatus("🔴 Detenido");
            
            Application.MainLoop.Invoke(() =>
            {
                var buttons = Application.Top.Subviews[0].Subviews.OfType<Button>().ToList();
                if (buttons.Count >= 2)
                {
                    buttons[0].Enabled = true;  // Start
                    buttons[1].Enabled = false; // Stop
                }
            });
        }
    }

    static void HighlightLocation(int index)
    {
        Application.MainLoop.Invoke(() =>
        {
            if (_locationsList != null)
            {
                _locationsList.SelectedItem = index;
            }
        });
    }

    static void UpdateStatus(string status)
    {
        Application.MainLoop.Invoke(() =>
        {
            if (_statusLabel != null)
                _statusLabel.Text = status;
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
                    _countLabel.Text = $"📊 Propiedades en BD: {count:N0}";
                }
                catch (Exception ex)
                {
                    _countLabel.Text = $"📊 Propiedades en BD: Error ({ex.Message})";
                }
            }
        });
    }

    static void UpdateLastRun()
    {
        Application.MainLoop.Invoke(() =>
        {
            if (_lastRunLabel != null)
                _lastRunLabel.Text = $"⏰ Último ciclo: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
        });
    }

    static void Log(string message)
    {
        Application.MainLoop.Invoke(() =>
        {
            if (_logView != null)
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                _logView.Text += $"[{timestamp}] {message}\n";
                _logView.MoveEnd();
            }
        });
    }
}

public class LocationData
{
    public List<Location> Locations { get; set; } = new();
}