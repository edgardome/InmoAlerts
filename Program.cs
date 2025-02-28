using HtmlAgilityPack;
using Idealista.Helpers;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using static Idealista.Models.Models;

namespace Idealista
{
    public class IdealistaScraper
    {
        public static async Task<List<Property>> ScrapeProperty(string url, string apiKey)
        {
            var client = new HttpClient();
            var doc = new HtmlDocument();
            var requestUrl = url;
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);

            // Agregar los encabezados necesarios
            var headers = new Dictionary<string, string>
        {
            { "accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7" },
            { "accept-encoding", "gzip, deflate, br, zstd" },
            { "accept-language", "es-ES,es;q=0.9" },
            { "priority", "u=0, i" },
            { "sec-ch-ua", "\"Not A(Brand\";v=\"8\", \"Chromium\";v=\"132\", \"Google Chrome\";v=\"132\"" },
            { "sec-ch-ua-mobile", "?0" },
            { "sec-ch-ua-platform", "\"Windows\"" },
            { "sec-fetch-dest", "document" },
            { "sec-fetch-mode", "navigate" },
            { "sec-fetch-site", "none" },
            { "sec-fetch-user", "?1" },
            { "upgrade-insecure-requests", "1" },
            { "user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36" }
        };
            foreach (var header in headers)
            {
                requestMessage.Headers.Add(header.Key, header.Value);
            }

            HttpResponseMessage response = await client.SendAsync(requestMessage);
            string content = await Utils.DecodeGzip(response);
            string cityName = Regex.Match(content, @"<meta\s+name=""description""\s+content=""[^""]*en\s+([A-Za-zÀ-ÿ\s/]+),", RegexOptions.IgnoreCase).Groups[1].Value.Trim();

            doc.LoadHtml(content);

            // Lista para almacenar las propiedades extraídas
            var properties = new List<Property>();

            // Buscar todos los anuncios dentro de los elementos <article>
            var articles = doc.DocumentNode.SelectNodes("//article[contains(@class, 'item')]");

            if (articles != null)
            {
                foreach (var article in articles)
                {
                    HtmlDocument articleDoc = new HtmlDocument();
                    articleDoc.LoadHtml(article.InnerHtml);
                    var property = new Property();

                    property.Id = Regex.Match(article.InnerHtml, @"/inmueble/(\d+)/").Groups[1].Value;
                    property.Url = $"https://www.idealista.com/inmueble/{property.Id}/";

                    url = $"https://www.idealista.com/es/ajax/listingController/adContactInfoForListing.ajax?adId={property.Id}";
                    requestMessage = new HttpRequestMessage(HttpMethod.Get, url);

                    // Agregar los encabezados necesarios
                    headers = new Dictionary<string, string>
                {
                    { "accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7" },
                    { "accept-encoding", "gzip, deflate, br, zstd" },
                    { "accept-language", "es-ES,es;q=0.9" },
                    { "sec-ch-ua", "\"Not A(Brand\";v=\"8\", \"Chromium\";v=\"132\", \"Google Chrome\";v=\"132\"" },
                    { "sec-ch-ua-mobile", "?0" },
                    { "sec-ch-ua-platform", "\"Windows\"" },
                    { "sec-fetch-dest", "document" },
                    { "sec-fetch-mode", "navigate" },
                    { "sec-fetch-site", "none" },
                    { "sec-fetch-user", "?1" },
                    { "upgrade-insecure-requests", "1" },
                    { "user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36" }
                };
                    foreach (var header in headers)
                    {
                        requestMessage.Headers.Add(header.Key, header.Value);
                    }

                    await Task.Delay(new Random().Next(3000, 10001));
                    response = await client.SendAsync(requestMessage);
                    content = await Utils.DecodeGzip(response);
                    dynamic propertyJson = JsonConvert.DeserializeObject<dynamic>(content);

                    property.IsProfessional = propertyJson.data.isAdProfessional;
                    property.Title = propertyJson.data.shortDescription != null ? propertyJson.data.shortDescription : "";
                    property.Location = cityName;
                    property.Currency = "€";
                    string priceText = articleDoc.DocumentNode.SelectSingleNode("//span[contains(@class,'item-price')]")?.InnerText.Trim().Replace(",", "").Replace("€", "").Replace(".", "") ?? "0";
                    property.Price = int.TryParse(priceText, out int price) ? price : 0;
                    property.Description = articleDoc.DocumentNode.SelectSingleNode("//div[contains(@class,'item-description')]//p")?.InnerText.Trim() ?? "";

                    if (!property.IsProfessional && !DatabaseHelper.PropertyExists(property.Id))
                    {
                        DatabaseHelper.InsertProperty(property);
                        EmailHelper.SendPropertyEmail(property);
                    }

                    properties.Add(property);
                }
            }

            return properties;
        }

        public static async Task<List<Property>> ScrapeProperties(string url, string apiKey)
        {
            var properties = ScrapeProperty(url, apiKey);
            return await properties;
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            // Reemplaza con tu clave de API de Scrapfly
            string apiKey = "";

            // URL a escrapear
            string url = "https://www.idealista.com/venta-viviendas/madrid-madrid/?ordenado-por=fecha-publicacion-desc";

            //Inicializamos la base de datos
            DatabaseHelper.InitializeDatabase();

            // Creamos un temporizador que se ejecuta cada hora
            var timer = new PeriodicTimer(TimeSpan.FromHours(1));

            try
            {
                do
                {
                    Console.WriteLine($"Iniciando scraping: {DateTime.Now}");

                    try
                    {
                        var properties = await IdealistaScraper.ScrapeProperties(url, apiKey);

                        Console.WriteLine("--------------------------");
                        foreach (var property in properties)
                        {
                            if (!property.IsProfessional)
                            {
                                Console.WriteLine($"Property ID: {property.Id}");
                                Console.WriteLine($"Property URL: {property.Url}");
                                Console.WriteLine($"Title: {property.Title}");
                                Console.WriteLine($"Location: {property.Location}");
                                Console.WriteLine($"Price: {property.Price} {property.Currency}");
                                Console.WriteLine($"Description: {property.Description}");
                                Console.WriteLine("--------------------------");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error durante el scraping: {ex.Message}");
                    }

                    Console.WriteLine($"Esperando hasta la próxima ejecución...");
                }
                while (await timer.WaitForNextTickAsync());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fatal: {ex.Message}");
            }
        }
    }
}