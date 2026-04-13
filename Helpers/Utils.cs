using Idealista.Models;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Idealista.Helpers
{
    class Utils
    {
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
                { "user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36" }
            };

            foreach (var header in DefaultHeaders)
            {
                requestMessage.Headers.Add(header.Key, header.Value);
            }
        }

        public static bool IsPropertyOccupied(string description)
        {
            string[] occupiedKeywords = new[]
            {
                "ocupado",
                "ocupada",
                "ocupacional",
                "sin posesion",
                "inquilinos",
                "alquilado",
                "alquilada",
                "rentado",
                "rentada",
                "con inquilino",
                "con inquilinos",
                "con renta",
                "arrendado",
                "arrendada"
            };

            string normalizedDescription = RemoveDiacritics(description.ToLowerInvariant());

            return occupiedKeywords.Any(keyword =>
                normalizedDescription.Contains(RemoveDiacritics(keyword.ToLowerInvariant())));
        }

        public static string GetVendorType(Property property)
        {
            if (property.IsBank) return "Banco";
            if (property.IsProfessional) return "Profesional";
            return "Particular";
        }

        private static string RemoveDiacritics(string text)
        {
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}