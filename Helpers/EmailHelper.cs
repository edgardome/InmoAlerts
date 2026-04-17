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
                    Subject = $"Nueva propiedad: {property.Title} - {property.Price}{property.Currency}",
                    IsBodyHtml = true
                };

                string vendorType = property.IsBank ? "Banco" : 
                                   property.IsProfessional ? "Profesional" : "Particular";

                string htmlBody = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                        <div style='text-align: center; margin-bottom: 20px; padding: 20px; background-color: #f8f9fa;'>
                            <img src='cid:InmoAlertLogo' alt='InmoAlert Logo' style='max-width: 200px; height: auto;'/>
                        </div>
                        <div style='padding: 20px; background-color: #ffffff; border: 1px solid #dee2e6; border-radius: 5px;'>
                            <h2 style='color: #007bff; margin-top: 0;'>{property.Title}</h2>
                            
                            <div style='margin: 15px 0;'>
                                <p style='margin: 5px 0;'><strong>📍 Ubicación:</strong> {property.Location}, {property.Province}</p>
                                <p style='margin: 5px 0;'><strong>💰 Precio:</strong> <span style='color: #28a745; font-size: 1.3em; font-weight: bold;'>{property.Price:N0} {property.Currency}</span></p>
                                <p style='margin: 5px 0;'><strong>🏢 Tipo de vendedor:</strong> {vendorType}</p>
                                <p style='margin: 5px 0;'><strong>🆔 ID:</strong> {property.Id}</p>
                            </div>
                            
                            <div style='margin: 20px 0; padding: 15px; background-color: #f8f9fa; border-left: 4px solid #007bff;'>
                                <p style='margin: 0;'><strong>📝 Descripción:</strong></p>
                                <p style='margin: 10px 0 0 0;'>{property.Description}</p>
                            </div>
                            
                            <div style='text-align: center; margin-top: 25px;'>
                                <a href='{property.Url}' style='display: inline-block; padding: 12px 30px; background-color: #007bff; color: white; text-decoration: none; border-radius: 5px; font-weight: bold;'>
                                    Ver en Idealista
                                </a>
                            </div>
                        </div>
                        <div style='text-align: center; margin-top: 20px; color: #6c757d; font-size: 0.9em;'>
                            <p>InmoAlert - Alertas automáticas de propiedades</p>
                        </div>
                    </div>";

                var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, null, MediaTypeNames.Text.Html);

                var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "inmoalert_logo.png");
                if (File.Exists(logoPath))
                {
                    var logo = new LinkedResource(logoPath, MediaTypeNames.Image.Png)
                    {
                        ContentId = "InmoAlertLogo"
                    };
                    htmlView.LinkedResources.Add(logo);
                }

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
    }
}