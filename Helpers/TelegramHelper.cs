using InmoAlerts.Configuration;
using InmoAlerts.Models;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace InmoAlerts.Helpers
{
    public static class TelegramHelper
    {
        private static readonly string _botToken;
        private static readonly HttpClient _http = new();

        public static int TelegramsSentSession { get; private set; } = 0;

        static TelegramHelper()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _botToken = configuration.GetSection("TelegramSettings")
                .Get<TelegramSettings>()?.BotToken ?? string.Empty;
        }

        /// <summary>
        /// Sends a new-property alert to a Telegram chat.
        /// Does nothing if BotToken is empty or chatId is null/empty.
        /// </summary>
        public static async Task SendPropertyAlert(string? chatId, Property property)
        {
            if (string.IsNullOrWhiteSpace(_botToken) || string.IsNullOrWhiteSpace(chatId))
                return;

            try
            {
                bool isAlquiler = property.ListingType == "Alquiler";
                string typeEmoji  = isAlquiler ? "🏠" : "🏷";
                string typeLabel  = isAlquiler ? "ALQUILER" : "VENTA";
                string priceStr   = isAlquiler
                    ? $"{property.Price:N0} €/mes"
                    : $"{property.Price:N0} €";
                string sellerType = property.IsBank        ? "🏦 Banco"
                                  : property.IsProfessional ? "🏢 Agencia"
                                                            : "👤 Particular ✅";

                string text = $"{typeEmoji} *NUEVO INMUEBLE — {typeLabel}*\n" +
                              $"━━━━━━━━━━━━━━━━━━━━━\n" +
                              $"📝 {EscapeMd(property.Title)}\n" +
                              $"💰 *{EscapeMd(priceStr)}*\n" +
                              $"📍 {EscapeMd(property.Location)}, {EscapeMd(property.Province)}\n" +
                              $"🔎 {sellerType}\n" +
                              $"━━━━━━━━━━━━━━━━━━━━━\n" +
                              $"[Ver en Idealista →]({property.Url})";

                await SendMessageAsync(chatId, text);
                TelegramsSentSession++;
                Console.WriteLine($"✈ Telegram enviado [nueva propiedad] a chat {chatId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error enviando Telegram (nueva propiedad): {ex.Message}");
            }
        }

        /// <summary>
        /// Sends a price-change alert to a Telegram chat.
        /// </summary>
        public static async Task SendPriceChangeAlert(string? chatId, Property property, int oldPrice)
        {
            if (string.IsNullOrWhiteSpace(_botToken) || string.IsNullOrWhiteSpace(chatId))
                return;

            try
            {
                bool isAlquiler   = property.ListingType == "Alquiler";
                bool isPriceDrop  = property.Price < oldPrice;
                string perMonth   = isAlquiler ? "/mes" : "";
                string arrow      = isPriceDrop ? "⬇" : "⬆";
                string changeWord = isPriceDrop ? "BAJADA" : "SUBIDA";
                int diff          = Math.Abs(property.Price - oldPrice);

                string text = $"{arrow} *{changeWord} DE PRECIO*\n" +
                              $"━━━━━━━━━━━━━━━━━━━━━\n" +
                              $"📝 {EscapeMd(property.Title)}\n" +
                              $"~~{oldPrice:N0} €{perMonth}~~ → *{property.Price:N0} €{perMonth}*\n" +
                              $"{arrow} {(isPriceDrop ? "-" : "+")}{diff:N0} €\n" +
                              $"📍 {EscapeMd(property.Location)}, {EscapeMd(property.Province)}\n" +
                              $"━━━━━━━━━━━━━━━━━━━━━\n" +
                              $"[Ver en Idealista →]({property.Url})";

                await SendMessageAsync(chatId, text);
                TelegramsSentSession++;
                Console.WriteLine($"✈ Telegram enviado [cambio precio] a chat {chatId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error enviando Telegram (cambio precio): {ex.Message}");
            }
        }

        private static async Task SendMessageAsync(string chatId, string text)
        {
            var url     = $"https://api.telegram.org/bot{_botToken}/sendMessage";
            var payload = new { chat_id = chatId, text, parse_mode = "MarkdownV2", disable_web_page_preview = false };
            var json    = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new Exception($"Telegram API error {response.StatusCode}: {body}");
            }
        }

        /// <summary>Escapes special characters required by Telegram MarkdownV2.</summary>
        private static string EscapeMd(string text)
        {
            // Characters that must be escaped in MarkdownV2
            var special = new[] { '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (Array.IndexOf(special, c) >= 0)
                    sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
