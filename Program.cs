using Idealista.Helpers;
using Newtonsoft.Json;
using static Idealista.Models.Models;
using HtmlAgilityPack;
using System.Text.RegularExpressions;
using Idealista;

class Program
{
    private static readonly HttpClient client = new HttpClient(); // Declaración del HttpClient
    private static volatile bool _dbOperationsRunning = true;
    private static readonly SemaphoreSlim _consoleLock = new(1, 1);

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

    private static async Task<List<Property>> ScrapeProperties(City city)
    {
        try
        {
            if (string.IsNullOrEmpty(city.Url))
                throw new ArgumentException("La URL no puede estar vacía", nameof(city.Url));

            Console.WriteLine($"Iniciando scraping de: {city.Url}");
            var startTime = DateTime.Now;

            var doc = new HtmlDocument();
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, city.Url);

            // Agregar los encabezados necesarios
            Utils.SetDefaultHeaders(requestMessage);

            HttpResponseMessage response = await client.SendAsync(requestMessage);
            string content = await Utils.DecodeGzip(response);
            string cityName = city.Name;

            doc.LoadHtml(content);
            var properties = new List<Property>();
            var articles = doc.DocumentNode.SelectNodes("//article[contains(@class, 'item')]");

            if (articles != null)
            {
                foreach (var article in articles)
                {
                    try
                    {
                        // Añadir delay entre artículos
                        await Task.Delay(TimeSpan.FromSeconds(2));

                        var property = await ProcessArticle(article, cityName);
                        if (property != null)
                        {
                            properties.Add(property);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error procesando artículo: {ex.Message}");
                    }
                }
            }

            var duration = DateTime.Now - startTime;
            Console.WriteLine($"Scraping completado. Duración: {duration.TotalSeconds:F2} segundos. Propiedades encontradas: {properties.Count}");

            return properties;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en ScrapeProperties: {ex.Message}");
            throw;
        }
    }

    private static async Task<Property?> ProcessArticle(HtmlNode article, string cityName)
    {
        HtmlDocument articleDoc = new HtmlDocument();
        articleDoc.LoadHtml(article.InnerHtml);
        var property = new Property();

        property.Id = Regex.Match(article.InnerHtml, @"/inmueble/(\d+)/").Groups[1].Value;
        property.Url = $"https://www.idealista.com/inmueble/{property.Id}/";

        var url = $"https://www.idealista.com/es/ajax/listingController/adContactInfoForListing.ajax?adId={property.Id}";
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);

        Utils.SetDefaultHeaders(requestMessage);

        await Task.Delay(new Random().Next(3000, 10001));
        var response = await client.SendAsync(requestMessage);
        var content = await Utils.DecodeGzip(response);

        dynamic? propertyJson = JsonConvert.DeserializeObject<dynamic>(content);

        if (propertyJson == null) return null;

        property.IsProfessional = propertyJson?.data?.isAdProfessional ?? false;
        property.Title = propertyJson?.data?.shortDescription ?? "";
        property.Location = cityName;
        property.Currency = "€";
        string priceText = articleDoc.DocumentNode.SelectSingleNode("//span[contains(@class,'item-price')]")?.InnerText.Trim().Replace(",", "").Replace("€", "").Replace(".", "") ?? "0";
        property.Price = int.TryParse(priceText, out int price) ? price : 0;
        property.Description = articleDoc.DocumentNode.SelectSingleNode("//div[contains(@class,'item-description')]//p")?.InnerText.Trim() ?? "";

        if (!property.IsProfessional && !DatabaseHelper.PropertyExists(property.Id))
        {
            DatabaseHelper.InsertProperty(property);
            await EmailHelper.SendPropertyEmail(property);
        }

        return property;
    }

    private static async Task ProcessCity(City city)
    {
        try
        {
            await _consoleLock.WaitAsync();
            Console.WriteLine($"\nProcesando ciudad: {city.Name}");
            _consoleLock.Release();

            var properties = await ScrapeProperties(city);

            await _consoleLock.WaitAsync();
            Console.WriteLine($"--------------------------");
            Console.WriteLine($"Resultados para {city.Name}:");
            Console.WriteLine($"--------------------------");

            foreach (var property in properties.Where(p => !p.IsProfessional))
            {
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
            Console.WriteLine($"Error procesando {city.Name}: {ex.Message}");
            _consoleLock.Release();
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

            await Task.Run(() => DatabaseHelper.InsertClient(client));
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

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(city))
            {
                Console.WriteLine("ID del cliente y ciudad son requeridos");
                return;
            }

            var subscription = new Subscription
            {
                Id = Guid.NewGuid().ToString(),
                ClientId = clientId,
                City = city
            };

            await Task.Run(() => DatabaseHelper.InsertSubscription(subscription));
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
            var clients = await Task.Run(() => DatabaseHelper.GetAllClients());
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
            var subscriptions = await Task.Run(() => DatabaseHelper.GetAllSubscriptions());
            Console.WriteLine("\nSuscripciones actuales:");
            Console.WriteLine("-----------------");
            foreach (var sub in subscriptions)
            {
                Console.WriteLine($"ID: {sub.Id}");
                Console.WriteLine($"Cliente ID: {sub.ClientId}");
                Console.WriteLine($"Ciudad: {sub.City}");
                Console.WriteLine("-----------------");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al obtener suscripciones: {ex.Message}");
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
            string citiesJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "cities.json");
            if (!File.Exists(citiesJsonPath))
            {
                Console.WriteLine("Error: No se encuentra el archivo Resources/cities.json");
                return;
            }

            string citiesJson = File.ReadAllText(citiesJsonPath);
            var citiesConfig = JsonConvert.DeserializeObject<CitiesConfig>(citiesJson);

            if (citiesConfig?.Cities == null || !citiesConfig.Cities.Any())
            {
                Console.WriteLine("No se encontraron ciudades para procesar en cities.json");
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

                    // Procesar ciudades una a una
                    foreach (var city in citiesConfig.Cities)
                    {
                        await ProcessCity(city);
                        await _consoleLock.WaitAsync();
                        Console.WriteLine($"Esperando 30 segundos antes de la siguiente ciudad...");
                        _consoleLock.Release();
                        await Task.Delay(TimeSpan.FromSeconds(30));
                    }

                    await _consoleLock.WaitAsync();
                    Console.WriteLine($"Ciclo completado. Esperando 1 hora hasta la próxima ejecución...");
                    _consoleLock.Release();

                    // Esperar una hora antes del siguiente ciclo
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