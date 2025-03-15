// Idealista.Helpers/ScraperHelper.cs
using Idealista.Configuration;
using Idealista.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Idealista.Helpers
{
    public static class ScraperHelper
    {
        private static readonly ScraperSettings _scraperSettings;
        private static readonly HttpClient client = new HttpClient();

        static ScraperHelper()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _scraperSettings = configuration.GetSection("ScraperSettings").Get<ScraperSettings>()
                ?? throw new InvalidOperationException("Scraper settings not found");
        }

        public static async Task<List<Property>> ScrapeProperties(Location location)
        {
            try
            {
                if (string.IsNullOrEmpty(location.Url))
                    throw new ArgumentException("La URL no puede estar vacía", nameof(location.Url));

                Console.WriteLine($"Iniciando scraping de: {location.Url}");
                var startTime = DateTime.Now;

                var doc = new HtmlDocument();
                var requestMessage = new HttpRequestMessage(HttpMethod.Get, location.Url);

                // Agregar los encabezados necesarios
                Utils.SetDefaultHeaders(requestMessage);

                HttpResponseMessage response = await client.SendAsync(requestMessage);
                string content = await Utils.DecodeGzip(response);
                string name = location.Name;

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

                            var property = await ProcessArticle(article, name);
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

        private static async Task<Property?> ProcessArticle(HtmlNode article, string location)
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
            property.IsBank = propertyJson?.data?.agencyIsABank ?? false;
            property.Title = propertyJson?.data?.shortDescription ?? "";
            property.Location = location;
            property.Currency = "€";
            string priceText = articleDoc.DocumentNode.SelectSingleNode("//span[contains(@class,'item-price')]")?.InnerText.Trim().Replace(",", "").Replace("€", "").Replace(".", "") ?? "0";
            property.Price = int.TryParse(priceText, out int price) ? price : 0;
            property.Description = articleDoc.DocumentNode.SelectSingleNode("//div[contains(@class,'item-description')]//p")?.InnerText.Trim() ?? "";

            if (!DatabaseHelper.PropertyExists(property.Id))
            {
                DatabaseHelper.InsertProperty(property);
                await EmailHelper.SendPropertyEmail(property);
            }

            return property;
        }
    }
}