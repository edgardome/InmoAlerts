using Idealista.Helpers;
using Idealista.Models;
using Idealista.Services;
using Newtonsoft.Json;

class Program
{
    private static readonly HttpClient client = new HttpClient();
    private static volatile bool _dbOperationsRunning = true;
    private static readonly SemaphoreSlim _consoleLock = new(1, 1);

    private static readonly ClientService _clientService = new();
    private static readonly SubscriptionService _subscriptionService = new();
    private static readonly PropertyService _propertyService = new();

    private static async Task HandleDatabaseOperations()
    {
        while (_dbOperationsRunning)
        {
            await _consoleLock.WaitAsync();
            try
            {
                Console.WriteLine("\nOperaciones disponibles:");
                Console.WriteLine("1. Crear nuevo cliente");
                Console.WriteLine("2. Crear nueva suscripción");
                Console.WriteLine("3. Ver clientes actuales");
                Console.WriteLine("4. Ver suscripciones");
                Console.WriteLine("5. Salir del menú de operaciones");
                Console.Write("Seleccione una opción: ");

                string? input = Console.ReadLine();

                switch (input)
                {
                    case "1":
                        await CreateNewClient();
                        break;
                    case "2":
                        await CreateNewSubscription();
                        break;
                    case "3":
                        await ViewClients();
                        break;
                    case "4":
                        await ViewSubscriptions();
                        break;
                    case "5":
                        _dbOperationsRunning = false;
                        Console.WriteLine("Saliendo del menú de operaciones. El scraping continuará ejecutándose.");
                        break;
                    default:
                        Console.WriteLine("Opción no válida");
                        break;
                }
            }
            finally
            {
                _consoleLock.Release();
            }

            await Task.Delay(100);
        }
    }

    private static async Task CreateNewClient()
    {
        try
        {
            Console.Write("Nombre del cliente: ");
            string? name = Console.ReadLine();
            Console.Write("Email del cliente: ");
            string? email = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
            {
                Console.WriteLine("Nombre y email son requeridos");
                return;
            }

            var client = new Client
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Email = email
            };

            await _clientService.InsertClientAsync(client);
            Console.WriteLine($"Cliente creado con ID: {client.Id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al crear cliente: {ex.Message}");
        }
    }

    private static async Task CreateNewSubscription()
    {
        try
        {
            await ViewClients();

            Console.Write("ID del cliente: ");
            string? clientId = Console.ReadLine();
            Console.Write("Ciudad para la suscripción: ");
            string? city = Console.ReadLine();
            Console.Write("Precio máximo (Enter para sin límite): ");
            string? maxPriceStr = Console.ReadLine();
            Console.Write("¿Incluir pisos ocupados? (s/N): ");
            string? includeOccupiedStr = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(city))
            {
                Console.WriteLine("ID del cliente y ciudad son requeridos");
                return;
            }

            var subscription = new Subscription
            {
                Id = Guid.NewGuid().ToString(),
                ClientId = clientId,
                Location = city,
                MaxPrice = string.IsNullOrWhiteSpace(maxPriceStr) ? 1000000 : int.Parse(maxPriceStr),
                IncludeOccupied = includeOccupiedStr?.Trim().ToLower() == "s"
            };

            await _subscriptionService.InsertSubscriptionAsync(subscription);
            Console.WriteLine($"Suscripción creada con ID: {subscription.Id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al crear suscripción: {ex.Message}");
        }
    }

    private static async Task ViewClients()
    {
        try
        {
            var clients = await _clientService.GetAllClientsAsync();
            Console.WriteLine("\nClientes actuales:");
            Console.WriteLine("-----------------");
            foreach (var client in clients)
            {
                Console.WriteLine($"ID: {client.Id}");
                Console.WriteLine($"Nombre: {client.Name}");
                Console.WriteLine($"Email: {client.Email}");
                Console.WriteLine("-----------------");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al obtener clientes: {ex.Message}");
        }
    }

    private static async Task ViewSubscriptions()
    {
        try
        {
            var subscriptions = await _subscriptionService.GetAllSubscriptionsAsync();
            Console.WriteLine("\nSuscripciones actuales:");
            Console.WriteLine("-----------------");
            foreach (var sub in subscriptions)
            {
                Console.WriteLine($"ID: {sub.Id}");
                Console.WriteLine($"Cliente ID: {sub.ClientId}");
                Console.WriteLine($"Ciudad: {sub.Location}");
                Console.WriteLine("-----------------");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al obtener suscripciones: {ex.Message}");
        }
    }

    private static async Task ProcessLocation(Location location)
    {
        try
        {
            await _consoleLock.WaitAsync();
            Console.WriteLine($"\nProcesando localizacion: {location.Name}");
            _consoleLock.Release();

            var properties = await ScraperHelper.ScrapeProperties(location);

            await _consoleLock.WaitAsync();
            Console.WriteLine($"--------------------------");
            Console.WriteLine($"Resultados para {location.Name}:");
            Console.WriteLine($"--------------------------");

            foreach (var property in properties.Where(p => !p.IsProfessional))
            {
                await _propertyService.ProcessPropertyAsync(property);
                Console.WriteLine($"Property ID: {property.Id}");
                Console.WriteLine($"Property URL: {property.Url}");
                Console.WriteLine($"Title: {property.Title}");
                Console.WriteLine($"Location: {property.Location}");
                Console.WriteLine($"Price: {property.Price} {property.Currency}");
                Console.WriteLine($"Description: {property.Description}");
                Console.WriteLine($"--------------------------");
            }
            _consoleLock.Release();
        }
        catch (Exception ex)
        {
            await _consoleLock.WaitAsync();
            Console.WriteLine($"Error procesando {location.Name}: {ex.Message}");
            _consoleLock.Release();
        }
    }

    static async Task Main(string[] args)
    {
        Console.WriteLine("Iniciando el sistema...");

        try
        {
            // Inicializar base de datos
            DatabaseHelper.InitializeDatabase();

            // Cargar configuración
            string locationsJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "locations.json");
            if (!File.Exists(locationsJsonPath))
            {
                Console.WriteLine("Error: No se encuentra el archivo Resources/locations.json");
                return;
            }

            string locationsJson = File.ReadAllText(locationsJsonPath);
            var locationsConfig = JsonConvert.DeserializeObject<LocationsConfig>(locationsJson);

            if (locationsConfig?.Locations == null || !locationsConfig.Locations.Any())
            {
                Console.WriteLine("No se encontraron localizaciones para procesar en locations.json");
                return;
            }

            // Iniciar operaciones de base de datos en segundo plano
            var dbOperationsTask = HandleDatabaseOperations();

            // Bucle infinito para el scraping
            while (true)
            {
                try
                {
                    await _consoleLock.WaitAsync();
                    Console.WriteLine($"\nIniciando scraping: {DateTime.Now}");
                    _consoleLock.Release();

                    // Procesar localizaciones una a una
                    foreach (var location in locationsConfig.Locations)
                    {
                        await ProcessLocation(location);
                        await _consoleLock.WaitAsync();
                        Console.WriteLine($"Esperando 30 segundos antes de la siguiente localizacion...");
                        _consoleLock.Release();
                        await Task.Delay(TimeSpan.FromSeconds(30));
                    }

                    await _consoleLock.WaitAsync();
                    Console.WriteLine($"Ciclo completado. Esperando 1 hora hasta la próxima ejecución...");
                    _consoleLock.Release();

                    await Task.Delay(TimeSpan.FromHours(1));
                }
                catch (Exception ex)
                {
                    await _consoleLock.WaitAsync();
                    Console.WriteLine($"Error durante el ciclo de scraping: {ex.Message}");
                    Console.WriteLine("Reintentando en 1 minuto...");
                    _consoleLock.Release();
                    await Task.Delay(TimeSpan.FromMinutes(1));
                }
            }
        }
        catch (Exception ex)
        {
            await _consoleLock.WaitAsync();
            Console.WriteLine($"Error fatal: {ex.Message}");
            _consoleLock.Release();
        }
        finally
        {
            _dbOperationsRunning = false;
            Console.WriteLine("Programa finalizado.");
        }
    }
}