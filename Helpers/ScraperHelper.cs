// InmoAlerts.Helpers/ScraperHelper.cs
using InmoAlerts.Configuration;
using InmoAlerts.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using Microsoft.Playwright;
using System.Diagnostics;

namespace InmoAlerts.Helpers
{
    public static class ScraperHelper
    {
        private static readonly ScraperSettings _scraperSettings;
        private static IPlaywright? _playwright;
        private static IBrowser? _browser;
        private static IBrowserContext? _context;
        private static bool _browsersInstalled = false;

        // Global semaphore to prevent multiple parallel scrapers from eating all RAM
        private static readonly SemaphoreSlim _scrapeSemaphore = new SemaphoreSlim(1, 1);

        public static Action<Property>? OnNewPropertyFound { get; set; }
        public static Action<Property, int>? OnPriceChanged { get; set; } // (property, oldPrice)

        // Detects listing type from URL if not explicitly provided
        public static string DetectListingType(string url) =>
            url.Contains("alquiler", StringComparison.OrdinalIgnoreCase) ? "Alquiler" : "Venta";

        /// <summary>
        /// Appends ?ordenado-por=fecha-publicacion-desc (or &amp;...) to a URL so
        /// Idealista returns the most recent listings first. This is the key optimisation
        /// that enables early-stop: once we hit a known property we can break the loop.
        /// </summary>
        private static string AddSortByRecentSuffix(string url)
        {
            const string param = "ordenado-por=fecha-publicacion-desc";
            if (url.Contains(param, StringComparison.OrdinalIgnoreCase))
                return url; // already present
            return url.Contains('?') ? $"{url}&{param}" : $"{url}?{param}";
        }

        static ScraperHelper()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _scraperSettings = configuration.GetSection("ScraperSettings").Get<ScraperSettings>()
                ?? throw new InvalidOperationException("Scraper settings not found");
        }

        private static async Task EnsureBrowsersInstalledAsync()
        {
            if (!_browsersInstalled)
            {
                Console.WriteLine("Instalando navegadores de Playwright (solo la primera vez)...");
                var exitCode = await Task.Run(() => Microsoft.Playwright.Program.Main(new[] { "install", "chromium" }));
                if (exitCode != 0)
                {
                    throw new Exception("Error al instalar los navegadores de Playwright");
                }
                _browsersInstalled = true;
                Console.WriteLine("Navegadores instalados correctamente");
            }
        }

        private static async Task EnsureBrowserContextAsync()
        {
            await EnsureBrowsersInstalledAsync();

            if (_playwright == null)
            {
                _playwright = await Playwright.CreateAsync();
                
                var headless = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Development" && 
                               Environment.GetEnvironmentVariable("HEADLESS") != "false";

                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = headless,
                    Args = new[] 
                    { 
                        "--disable-blink-features=AutomationControlled",
                        "--no-sandbox", 
                        "--disable-setuid-sandbox"
                    }
                });
            }

            if (_context == null)
            {
                _context = await _browser!.NewContextAsync(new BrowserNewContextOptions
                {
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                    Locale = "es-ES",
                    ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                    JavaScriptEnabled = true
                });

                // Stealth plugin avanzado para evadir DataDome
                await _context.AddInitScriptAsync(@"
                    Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
                    window.chrome = { runtime: {} };
                    Object.defineProperty(navigator, 'languages', { get: () => ['es-ES', 'es', 'en-US', 'en'] });
                    Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3] });
                    const originalQuery = window.navigator.permissions.query;
                    window.navigator.permissions.query = (parameters) => (
                        parameters.name === 'notifications' ?
                            Promise.resolve({ state: Notification.permission }) :
                            originalQuery(parameters)
                    );
                ");
            }
        }

        public static async Task<List<Property>> ScrapeProperties(Models.Location location, string? listingType = null)
        {
            listingType ??= DetectListingType(location.Url);

            // Build the effective URL — add sort-by-recent if requested
            string effectiveUrl = location.SortByRecent
                ? AddSortByRecentSuffix(location.Url)
                : location.Url;

            try
            {
                // Acquire semaphore to ensure only 1 browser tab works at a time
                await _scrapeSemaphore.WaitAsync();

                if (string.IsNullOrEmpty(effectiveUrl))
                    throw new ArgumentException("La URL no puede estar vacía", nameof(location.Url));

                Console.WriteLine($"Iniciando scraping [{listingType}] de: {effectiveUrl}");
                var startTime = DateTime.Now;

                var content = await ScrapeWithPlaywrightAsync(effectiveUrl);
                
                var doc = new HtmlDocument();
                doc.LoadHtml(content);
                string name = location.Name;

                var properties = new List<Property>();
                var articles   = doc.DocumentNode.SelectNodes("//article[contains(@class, 'item')]");

                if (articles != null)
                {
                    bool earlyStop = false;
                    foreach (var article in articles)
                    {
                        if (earlyStop) break;
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2));
                            var (property, alreadyKnown) = await ProcessArticle(article, location, listingType);
                            if (property != null)
                                properties.Add(property);

                            // Early-stop: list is ordered newest-first.
                            // If this property was already in the DB it means everything
                            // below it is even older — no point scraping further.
                            if (alreadyKnown && location.SortByRecent)
                                earlyStop = true;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error procesando artículo: {ex.Message}");
                        }
                    }
                }

                var duration = DateTime.Now - startTime;
                Console.WriteLine($"Scraping [{listingType}] completado. Duración: {duration.TotalSeconds:F2} segundos. Propiedades encontradas: {properties.Count}");

                return properties;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en ScrapeProperties: {ex.Message}");
                throw;
            }
            finally
            {
                _scrapeSemaphore.Release();
            }
        }

        private static async Task<string> ScrapeWithPlaywrightAsync(string url, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                IPage? page = null;
                try
                {
                    if (i > 0)
                    {
                        Console.WriteLine($"Reintentando ({i + 1}/{maxRetries})...");
                        await Task.Delay(new Random().Next(3000, 6000));
                    }

                    await EnsureBrowserContextAsync();

                    page = await _context!.NewPageAsync();

                    // Block network assets (images, css, fonts) to save massive amounts of RAM and CPU
                    await page.RouteAsync("**/*", async route =>
                    {
                        var t = route.Request.ResourceType;
                        if (t == "image" || t == "stylesheet" || t == "font" || t == "media")
                            await route.AbortAsync();
                        else
                            await route.ContinueAsync();
                    });

                    // Navega a la URL objetivo
                    Console.WriteLine($"Navegando a: {url}");
                    await page.GotoAsync(url, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 30000
                    });

                    await page.WaitForTimeoutAsync(1000);

                    // Hacer scroll suavemente para forzar la carga lazy de las imágenes y contenido
                    await page.EvaluateAsync(@"
                        async () => {
                            for(let i = 0; i < document.body.scrollHeight; i += 100) {
                                window.scrollTo(0, i);
                                await new Promise(r => setTimeout(r, 10));
                            }
                        }
                    ");

                    await page.WaitForTimeoutAsync(1000);

                    var html = await page.ContentAsync();

                    // Verificar si hay captcha o bloqueo de DataDome
                    if (html.Contains("se ha bloqueado", StringComparison.OrdinalIgnoreCase) ||
                        html.Contains("datadome", StringComparison.OrdinalIgnoreCase) ||
                        html.Contains("robot", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception("Anti-bot (DataDome) detectado en esta petición.");
                    }

                    Console.WriteLine("Página obtenida correctamente");
                    return html;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error en intento {i + 1}: {ex.Message}");
                    
                    if (i == maxRetries - 1)
                    {
                        throw new Exception($"Error después de {maxRetries} intentos: {ex.Message}", ex);
                    }
                }
                finally
                {
                    if (page != null)
                    {
                        await page.CloseAsync();
                    }
                }
            }

            throw new Exception("No se pudo completar el scraping");
        }

        /// <summary>
        /// Processes one listing article. Returns the Property (or null) and a flag indicating
        /// whether it was already known in the DB (used for early-stop when SortByRecent=true).
        /// </summary>
        private static async Task<(Property? property, bool alreadyKnown)> ProcessArticle(
            HtmlNode article, Models.Location location, string listingType)
        {
            HtmlDocument articleDoc = new HtmlDocument();
            articleDoc.LoadHtml(article.InnerHtml);
            var property = new Property();

            property.Id = Regex.Match(article.InnerHtml, @"/inmueble/(\d+)/").Groups[1].Value;
            property.Url = $"https://www.idealista.com/inmueble/{property.Id}/";

            var url = $"https://www.idealista.com/es/ajax/listingController/adContactInfoForListing.ajax?adId={property.Id}";
            
            var content = await GetArticleDetailsAsync(url);

            var propertyJson = JsonConvert.DeserializeObject<JObject>(content);
            if (propertyJson == null) return (null, false);

            var data = propertyJson["data"] as JObject;
            if (data == null) return (null, false);

            property.IsProfessional = data["isAdProfessional"]?.ToObject<bool?>() ?? false;
            property.IsBank = data["agencyIsABank"]?.ToObject<bool?>() ?? false;
            property.Title = data["shortDescription"]?.ToString() ?? "";
            property.Location = data["micrositeCityName"]?.ToString() ?? "";
            property.Province = location.Name;
            property.Currency = "€";
            property.ListingType = listingType;

            // Extract price: handles "150.000 €", "650 €/mes", "1.200 €/mes", etc.
            string priceRaw = articleDoc.DocumentNode
                .SelectSingleNode("//span[contains(@class,'item-price')]")?.InnerText.Trim() ?? "";
            var priceMatch = Regex.Match(priceRaw, @"[\d]+(?:[.,\s][\d]+)*");
            string priceClean = priceMatch.Success
                ? Regex.Replace(priceMatch.Value, @"[.,\s]", "")
                : "0";
            property.Price = int.TryParse(priceClean, out int price) ? price : 0;

            property.Description = articleDoc.DocumentNode.SelectSingleNode("//div[contains(@class,'item-description')]//p")?.InnerText.Trim() ?? "";

            // Only process properties from private sellers (no agencies or banks)
            if (property.IsProfessional || property.IsBank)
                return (property, DatabaseHelper.PropertyExists(property.Id));

            int? storedPrice = DatabaseHelper.GetPropertyPrice(property.Id);

            if (storedPrice == null)
            {
                // New property — alert via email + Telegram
                DatabaseHelper.InsertProperty(property);
                await EmailHelper.SendPropertyEmail(property);
                // Fire-and-forget Telegram to each chat ID configured for this location
                var chatId = location.TelegramChatId;
                await TelegramHelper.SendPropertyAlert(chatId, property);
                OnNewPropertyFound?.Invoke(property);
                return (property, false);
            }
            else if (storedPrice != property.Price && property.Price > 0)
            {
                // Price changed — update DB and alert
                DatabaseHelper.UpdatePropertyPrice(property.Id, property.Price);
                await EmailHelper.SendPriceChangeEmail(property, storedPrice.Value);
                var chatId = location.TelegramChatId;
                await TelegramHelper.SendPriceChangeAlert(chatId, property, storedPrice.Value);
                OnPriceChanged?.Invoke(property, storedPrice.Value);
                return (property, false);
            }

            // Property already in DB with same price — no change
            return (property, true);
        }

        private static async Task<string> GetArticleDetailsAsync(string url, int maxRetries = 3)
        {
            Exception? lastException = null;
            
            for (int i = 0; i < maxRetries; i++)
            {
                IPage? ajaxPage = null;
                try
                {
                    if (i > 0)
                    {
                        await Task.Delay(new Random().Next(2000, 4000));
                    }

                    await EnsureBrowserContextAsync();

                    // Espera aleatoria antes de la petición
                    await Task.Delay(new Random().Next(500, 1500));

                    // Crear una nueva página para la petición AJAX
                    ajaxPage = await _context!.NewPageAsync();

                    var response = await ajaxPage.GotoAsync(url, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 10000
                    });

                    if (response == null || !response.Ok)
                    {
                        throw new Exception($"Error en la petición AJAX: {response?.Status}");
                    }

                    var content = await ajaxPage.ContentAsync();

                    // Extraer el JSON del body
                    var jsonMatch = Regex.Match(content, @"<pre[^>]*>(.*?)</pre>", RegexOptions.Singleline);
                    if (jsonMatch.Success)
                    {
                        return jsonMatch.Groups[1].Value;
                    }

                    // Si no está en un tag pre, intentar obtener el body directamente
                    var bodyContent = Regex.Match(content, @"<body[^>]*>(.*?)</body>", RegexOptions.Singleline);
                    if (bodyContent.Success)
                    {
                        var cleanContent = bodyContent.Groups[1].Value.Trim();
                        // Intentar parsear como JSON para verificar
                        try
                        {
                            JsonConvert.DeserializeObject(cleanContent);
                            return cleanContent;
                        }
                        catch
                        {
                            // Si no es JSON válido, devolver el contenido completo
                            return content;
                        }
                    }

                    return content;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Console.WriteLine($"Error obteniendo detalles del artículo (intento {i + 1}): {ex.Message}");
                }
                finally
                {
                    if (ajaxPage != null)
                    {
                        await ajaxPage.CloseAsync();
                    }
                }
            }

            throw new Exception($"Error después de {maxRetries} intentos obteniendo detalles: {lastException?.Message}", lastException);
        }

        public static async Task DisposeAsync()
        {

            if (_context != null)
            {
                await _context.CloseAsync();
                await _context.DisposeAsync();
                _context = null;
            }

            if (_browser != null)
            {
                await _browser.CloseAsync();
                await _browser.DisposeAsync();
                _browser = null;
            }

            _playwright?.Dispose();
            _playwright = null;
        }
    }
}