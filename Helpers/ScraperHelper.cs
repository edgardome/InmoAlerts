// Idealista.Helpers/ScraperHelper.cs
using Idealista.Configuration;
using Idealista.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using Microsoft.Playwright;
using System.Diagnostics;

namespace Idealista.Helpers
{
    public static class ScraperHelper
    {
        private static readonly ScraperSettings _scraperSettings;
        private static IPlaywright? _playwright;
        private static IBrowser? _browser;
        private static IBrowserContext? _context;
        private static IPage? _mainPage;
        private static bool _browsersInstalled = false;

        public static Action<Property>? OnNewPropertyFound { get; set; }
        public static Action<Property, int>? OnPriceChanged { get; set; } // (property, oldPrice)

        // Detects listing type from URL if not explicitly provided
        public static string DetectListingType(string url) =>
            url.Contains("alquiler", StringComparison.OrdinalIgnoreCase) ? "Alquiler" : "Venta";

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
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = false,
                    Args = new[] 
                    { 
                        "--disable-blink-features=AutomationControlled",
                        "--window-position=-2400,-2400" // Mover la ventana fuera de la pantalla
                    }
                });

                // Minimizar usando PowerShell
                _ = Task.Run(() => MinimizeBrowserWindows());
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
            }

            if (_mainPage == null)
            {
                _mainPage = await _context.NewPageAsync();
            }
        }

        private static void MinimizeBrowserWindows()
        {
            try
            {
                // Esperar a que la ventana se cree
                Thread.Sleep(2000);

                // Usar PowerShell para minimizar todas las ventanas de Chrome
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = @"-Command ""
                        Add-Type @'
                            using System;
                            using System.Runtime.InteropServices;
                            public class Win32 {
                                [DllImport(""user32.dll"")]
                                public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
                                [DllImport(""user32.dll"")]
                                public static extern bool SetForegroundWindow(IntPtr hWnd);
                            }
'@
                        $processes = Get-Process | Where-Object {$_.ProcessName -like '*chrome*' -or $_.ProcessName -like '*msedge*'}
                        foreach ($proc in $processes) {
                            if ($proc.MainWindowHandle -ne [IntPtr]::Zero) {
                                [Win32]::ShowWindow($proc.MainWindowHandle, 6) | Out-Null
                            }
                        }
                    """,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(psi);
                Console.WriteLine("✓ Ventana del navegador minimizada");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ No se pudo minimizar: {ex.Message}");
            }
        }

        public static async Task<List<Property>> ScrapeProperties(Models.Location location, string? listingType = null)
        {
            listingType ??= DetectListingType(location.Url);
            try
            {
                if (string.IsNullOrEmpty(location.Url))
                    throw new ArgumentException("La URL no puede estar vacía", nameof(location.Url));

                Console.WriteLine($"Iniciando scraping [{listingType}] de: {location.Url}");
                var startTime = DateTime.Now;

                var content = await ScrapeWithPlaywrightAsync(location.Url);
                
                var doc = new HtmlDocument();
                doc.LoadHtml(content);
                string name = location.Name;

                var properties = new List<Property>();
                var articles = doc.DocumentNode.SelectNodes("//article[contains(@class, 'item')]");

                if (articles != null)
                {
                    foreach (var article in articles)
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2));
                            var property = await ProcessArticle(article, name, listingType);
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
                Console.WriteLine($"Scraping [{listingType}] completado. Duración: {duration.TotalSeconds:F2} segundos. Propiedades encontradas: {properties.Count}");

                return properties;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en ScrapeProperties: {ex.Message}");
                throw;
            }
        }

        private static async Task<string> ScrapeWithPlaywrightAsync(string url, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (i > 0)
                    {
                        Console.WriteLine($"Reintentando ({i + 1}/{maxRetries})...");
                        await Task.Delay(new Random().Next(3000, 6000));
                    }

                    await EnsureBrowserContextAsync();

                    // Visita la página principal primero solo si es el primer intento
                    if (i == 0)
                    {
                        Console.WriteLine("Visitando página principal de Idealista...");
                        await _mainPage!.GotoAsync("https://www.idealista.com", new PageGotoOptions
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                            Timeout = 30000
                        });

                        await _mainPage.WaitForTimeoutAsync(new Random().Next(2000, 4000));
                    }

                    // Navega a la URL objetivo
                    Console.WriteLine($"Navegando a: {url}");
                    await _mainPage!.GotoAsync(url, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 30000
                    });

                    await _mainPage.WaitForTimeoutAsync(1000);

                    var html = await _mainPage.ContentAsync();

                    // Verificar si hay captcha
                    if (html.Contains("se ha bloqueado", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception("Captcha detectado");
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
            }

            throw new Exception("No se pudo completar el scraping");
        }

        private static async Task<Property?> ProcessArticle(HtmlNode article, string location, string listingType)
        {
            HtmlDocument articleDoc = new HtmlDocument();
            articleDoc.LoadHtml(article.InnerHtml);
            var property = new Property();

            property.Id = Regex.Match(article.InnerHtml, @"/inmueble/(\d+)/").Groups[1].Value;
            property.Url = $"https://www.idealista.com/inmueble/{property.Id}/";

            var url = $"https://www.idealista.com/es/ajax/listingController/adContactInfoForListing.ajax?adId={property.Id}";
            
            var content = await GetArticleDetailsAsync(url);

            var propertyJson = JsonConvert.DeserializeObject<JObject>(content);
            if (propertyJson == null) return null;

            var data = propertyJson["data"] as JObject;
            if (data == null) return null;

            property.IsProfessional = data["isAdProfessional"]?.ToObject<bool?>() ?? false;
            property.IsBank = data["agencyIsABank"]?.ToObject<bool?>() ?? false;
            property.Title = data["shortDescription"]?.ToString() ?? "";
            property.Location = data["micrositeCityName"]?.ToString() ?? "";
            property.Province = location;
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

            if (!property.IsProfessional)
            {
                int? storedPrice = DatabaseHelper.GetPropertyPrice(property.Id);

                if (storedPrice == null)
                {
                    // New property
                    DatabaseHelper.InsertProperty(property);
                    await EmailHelper.SendPropertyEmail(property);
                    OnNewPropertyFound?.Invoke(property);
                }
                else if (storedPrice != property.Price && property.Price > 0)
                {
                    // Price changed — update DB and alert
                    DatabaseHelper.UpdatePropertyPrice(property.Id, property.Price);
                    await EmailHelper.SendPriceChangeEmail(property, storedPrice.Value);
                    OnPriceChanged?.Invoke(property, storedPrice.Value);
                }
            }

            return property;
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
            if (_mainPage != null)
            {
                await _mainPage.CloseAsync();
                _mainPage = null;
            }

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