using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using Idealista.Configuration;
using Idealista.Models;
using Microsoft.Extensions.Configuration;

namespace Idealista.Helpers
{
    public static class EmailHelper
    {
        private static readonly EmailSettings _emailSettings;
        public static int EmailsSentSession { get; private set; } = 0;

        static EmailHelper()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _emailSettings = configuration.GetSection("EmailSettings").Get<EmailSettings>()
                ?? throw new InvalidOperationException("Email settings not found");
        }

        public static async Task SendPropertyEmail(Property property)
        {
            try
            {
                if (_emailSettings.Recipients == null || _emailSettings.Recipients.Count == 0)
                {
                    Console.WriteLine("No hay destinatarios configurados en appsettings.json");
                    return;
                }

                using var smtpClient = new SmtpClient(_emailSettings.SmtpServer)
                {
                    Port = _emailSettings.Port,
                    Credentials = new NetworkCredential(_emailSettings.Username, _emailSettings.Password),
                    EnableSsl = true
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(_emailSettings.FromEmail),
                    Subject = $"[{property.ListingType.ToUpper()}] {property.Title} - {property.Price:N0}{property.Currency}{(property.ListingType == "Alquiler" ? "/mes" : "")}",
                    IsBodyHtml = true
                };

                string vendorType = property.IsBank ? "Banco" : 
                                   property.IsProfessional ? "Profesional" : "Particular";

                bool isAlquiler = property.ListingType == "Alquiler";
                string listingBadgeColor = isAlquiler ? "#6f42c1" : "#0969da";
                string listingLabel = isAlquiler ? "🏠 ALQUILER" : "🏷 VENTA";
                string priceLabel = isAlquiler ? "💰 Precio/mes" : "💰 Precio";
                string priceFormatted = isAlquiler
                    ? $"{property.Price:N0} {property.Currency}/mes"
                    : $"{property.Price:N0} {property.Currency}";

                string htmlBody = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                        <div style='padding: 20px; background-color: #ffffff; border: 1px solid #dee2e6; border-radius: 5px;'>
                            <div style='margin-bottom: 12px;'>
                                <span style='display:inline-block;padding:4px 12px;background-color:{listingBadgeColor};color:#fff;border-radius:20px;font-size:0.8em;font-weight:bold;'>{listingLabel}</span>
                            </div>
                            <h2 style='color: #007bff; margin-top: 0;'>{property.Title}</h2>
                            
                            <div style='margin: 15px 0;'>
                                <p style='margin: 5px 0;'><strong>📍 Ubicación:</strong> {property.Location}, {property.Province}</p>
                                <p style='margin: 5px 0;'><strong>{priceLabel}:</strong> <span style='color: #28a745; font-size: 1.3em; font-weight: bold;'>{priceFormatted}</span></p>
                                <p style='margin: 5px 0;'><strong>🏢 Tipo de vendedor:</strong> {vendorType}</p>
                                <p style='margin: 5px 0;'><strong>🆔 ID:</strong> {property.Id}</p>
                            </div>
                            
                            <div style='margin: 20px 0; padding: 15px; background-color: #f8f9fa; border-left: 4px solid #007bff;'>
                                <p style='margin: 0;'><strong>📝 Descripción:</strong></p>
                                <p style='margin: 10px 0 0 0;'>{property.Description}</p>
                            </div>
                            
                            <div style='text-align: center; margin-top: 25px;'>
                                <a href='{property.Url}' style='display: inline-block; padding: 12px 30px; background-color: {listingBadgeColor}; color: white; text-decoration: none; border-radius: 5px; font-weight: bold;'>
                                    Ver en Idealista
                                </a>
                            </div>
                        </div>
                        <div style='text-align: center; margin-top: 20px; color: #6c757d; font-size: 0.85em; padding-top: 12px; border-top: 1px solid #e9ecef;'>
                            <p style='margin: 0;'>NexumLabs · Alertas automáticas de propiedades</p>
                            <p style='margin: 4px 0 0;'><a href='https://nexumlabs.es' style='color: #0969da; text-decoration: none;'>nexumlabs.es</a></p>
                        </div>
                    </div>";

                var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, null, MediaTypeNames.Text.Html);

                mailMessage.AlternateViews.Add(htmlView);

                foreach (var email in _emailSettings.Recipients)
                {
                    mailMessage.Bcc.Add(email);
                }

                await smtpClient.SendMailAsync(mailMessage);
                EmailsSentSession++;
                Console.WriteLine($"✉ Email enviado para propiedad {property.Id} a {_emailSettings.Recipients.Count} destinatario(s)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error al enviar email: {ex.Message}");
            }
        }

        public static async Task SendPriceChangeEmail(Property property, int oldPrice)
        {
            try
            {
                if (_emailSettings.Recipients == null || _emailSettings.Recipients.Count == 0) return;

                using var smtpClient = new SmtpClient(_emailSettings.SmtpServer)
                {
                    Port = _emailSettings.Port,
                    Credentials = new NetworkCredential(_emailSettings.Username, _emailSettings.Password),
                    EnableSsl = true
                };

                bool isAlquiler = property.ListingType == "Alquiler";
                bool isPriceDrop = property.Price < oldPrice;
                int diff = Math.Abs(property.Price - oldPrice);
                string perMonth = isAlquiler ? "/mes" : "";
                string arrow = isPriceDrop ? "⬇" : "⬆";
                string changeLabel = isPriceDrop ? "BAJADA DE PRECIO" : "SUBIDA DE PRECIO";
                string changeColor = isPriceDrop ? "#1a7f37" : "#cf222e";
                string listingLabel = isAlquiler ? "🏠 ALQUILER" : "🏷 VENTA";
                string listingColor = isAlquiler ? "#8250df" : "#0969da";

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(_emailSettings.FromEmail),
                    Subject = $"[{changeLabel}] {property.Title} — {oldPrice:N0}€ → {property.Price:N0}€{perMonth}",
                    IsBodyHtml = true
                };

                string htmlBody = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                        <div style='padding: 20px; background-color: #ffffff; border: 1px solid #dee2e6; border-radius: 5px;'>
                            <div style='margin-bottom: 12px; display: flex; gap: 6px;'>
                                <span style='display:inline-block;padding:4px 12px;background-color:{listingColor};color:#fff;border-radius:20px;font-size:0.8em;font-weight:bold;'>{listingLabel}</span>
                                <span style='display:inline-block;padding:4px 12px;background-color:{changeColor};color:#fff;border-radius:20px;font-size:0.8em;font-weight:bold;'>{arrow} {changeLabel}</span>
                            </div>
                            <h2 style='color: #007bff; margin-top: 0;'>{property.Title}</h2>

                            <div style='margin: 20px 0; padding: 16px; background-color: #f8f9fa; border-radius: 8px; text-align: center;'>
                                <div style='font-size: 1em; color: #6c757d; text-decoration: line-through;'>{oldPrice:N0} €{perMonth}</div>
                                <div style='font-size: 1.8em; font-weight: bold; color: {changeColor};'>{property.Price:N0} €{perMonth}</div>
                                <div style='font-size: 0.9em; color: {changeColor}; margin-top: 4px;'>{arrow} {(isPriceDrop ? "-" : "+")}{diff:N0} € de diferencia</div>
                            </div>

                            <div style='margin: 15px 0;'>
                                <p style='margin: 5px 0;'><strong>📍 Ubicación:</strong> {property.Location}, {property.Province}</p>
                                <p style='margin: 5px 0;'><strong>🆔 ID:</strong> {property.Id}</p>
                            </div>

                            <div style='text-align: center; margin-top: 25px;'>
                                <a href='{property.Url}' style='display: inline-block; padding: 12px 30px; background-color: {changeColor}; color: white; text-decoration: none; border-radius: 5px; font-weight: bold;'>
                                    Ver en Idealista
                                </a>
                            </div>
                        </div>
                        <div style='text-align: center; margin-top: 20px; color: #6c757d; font-size: 0.85em; padding-top: 12px; border-top: 1px solid #e9ecef;'>
                            <p style='margin: 0;'>NexumLabs · Alertas automáticas de propiedades</p>
                            <p style='margin: 4px 0 0;'><a href='https://nexumlabs.es' style='color: #0969da; text-decoration: none;'>nexumlabs.es</a></p>
                        </div>
                    </div>";

                var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, null, MediaTypeNames.Text.Html);
                mailMessage.AlternateViews.Add(htmlView);

                foreach (var email in _emailSettings.Recipients)
                    mailMessage.Bcc.Add(email);

                await smtpClient.SendMailAsync(mailMessage);
                EmailsSentSession++;
                Console.WriteLine($"💰 Email de cambio de precio enviado para {property.Id}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error enviando email de cambio de precio: {ex.Message}");
            }
        }
    }
}