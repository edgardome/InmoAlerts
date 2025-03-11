using Idealista.Models;
using Newtonsoft.Json;
using System.IO.Compression;

namespace Idealista.Helpers
{
    class Utils
    {
        public class UserAgentRotator
        {
            private static readonly Random random = new Random();

            private static readonly string[] userAgents = new[]
            {
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:88.0) Gecko/20100101 Firefox/88.0",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/113.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Edge/112.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.114 Safari/537.36",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Firefox/89.0",
                "Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:89.0) Gecko/20100101 Firefox/89.0",
                "Mozilla/5.0 (Windows NT 6.1; WOW64; rv:34.0) Gecko/20100101 Firefox/34.0",
                "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
                "Mozilla/5.0 (Windows NT 6.1; rv:54.0) Gecko/20100101 Firefox/54.0",
                "Mozilla/5.0 (Windows NT 6.1; Win64; x64; Trident/7.0; AS-IA; rv:11.0) like Gecko",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/112.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Safari/537.36 Edge/89.0",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/90.0.4430.212 Safari/537.36 Edge/90.0",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:95.0) Gecko/20100101 Firefox/95.0",
                "Mozilla/5.0 (Linux; Android 11; SM-G991B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/94.0.4606.61 Mobile Safari/537.36",
                "Mozilla/5.0 (Linux; Android 9; SM-G973F) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/84.0.4147.89 Mobile Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:89.0) Gecko/20100101 Firefox/89.0",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_13_4) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/65.0.3325.181 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/71.0.3578.98 Safari/537.36 Edge/17.17134",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.36 Edge/16.16299",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/87.0.4280.67 Safari/537.36",
                "Mozilla/5.0 (Linux; Android 6.0.1; Nexus 5 Build/MOB30Z) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Mobile Safari/537.36",
                "Mozilla/5.0 (Windows NT 6.1; WOW64; rv:34.0) Gecko/20100101 Firefox/34.0",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_14_2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/74.0.3729.157 Safari/537.36",
                "Mozilla/5.0 (Linux; Android 8.1.0; SM-A530F) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/72.0.3626.121 Mobile Safari/537.36",
                "Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:54.0) Gecko/20100101 Firefox/54.0",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Safari/537.36 Edge/89.0",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Safari/537.36 Chrome/92.0.4515.159",
                "Mozilla/5.0 (Linux; Android 7.0; Nexus 6P Build/NBD91K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/89.0.4389.72 Mobile Safari/537.36",
                "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/69.0.3497.100 Safari/537.36",
                "Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:91.0) Gecko/20100101 Firefox/91.0",
                "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/93.0.4577.82 Safari/537.36",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_13_6) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/75.0.3770.142 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Safari/537.36 Edge/85.0",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/90.0.4430.212 Safari/537.36"
            };
            public static void SetRandomUserAgent(HttpRequestMessage requestMessage)
            {
                // Selecciona un user-agent aleatorio de la lista
                string randomUserAgent = userAgents[random.Next(userAgents.Length)];
                requestMessage.Headers.Add("user-agent", randomUserAgent);
            }
        }

        public static async Task<string> DecodeGzip(HttpResponseMessage response)
        {
            if (response.Content.Headers.ContentEncoding.Contains("gzip"))
            {
                using (var gzipStream = new GZipStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress))
                using (var reader = new StreamReader(gzipStream))
                {
                    string content = await reader.ReadToEndAsync();
                    return content;
                }
            }
            else
            {
                string content = await response.Content.ReadAsStringAsync();
                return content;
            }
        }

        public static void CreateNewClient()
        {
            var client = new Client
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Ernesto Fernandez",
                Email = "yt.eddyhd@gmail.com"
            };

            var subscriptions = new List<Subscription>
            {
                new Subscription
                {
                    Id = Guid.NewGuid().ToString(),
                    ClientId = client.Id,
                    Location = "Alcoy / Alcoi"
                },
                new Subscription
                {
                    Id = Guid.NewGuid().ToString(),
                    ClientId = client.Id,
                    Location = "Madrid"
                }
            };

            DatabaseHelper.ConfigureNewClient(client, subscriptions);
        }

        public static List<Location> LoadLocations()
        {
            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locations.json");
            if (File.Exists(jsonPath))
            {
                string jsonContent = File.ReadAllText(jsonPath);
                var locationsConfig = JsonConvert.DeserializeObject<LocationsConfig>(jsonContent);

                if (locationsConfig == null)
                {
                    throw new Exception("Error al cargar las localizaciones desde el archivo de configuración.");
                }

                return locationsConfig.Locations;
            }

            throw new FileNotFoundException("No se encontró el archivo de configuración de localizaciones.");
        }

        public static void SetDefaultHeaders(HttpRequestMessage requestMessage)
        {
            var DefaultHeaders = new Dictionary<string, string>
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

            foreach (var header in DefaultHeaders)
            {
                requestMessage.Headers.Add(header.Key, header.Value);
            }
        }
    }
}