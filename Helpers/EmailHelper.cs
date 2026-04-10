using System.Net.Mail;
using System.Net;
using System.Net.Mime;
using Idealista.Configuration;
using Idealista.Models;
using Microsoft.Extensions.Configuration;

namespace Idealista.Helpers
{
    public static class EmailHelper
    {
        private static readonly EmailSettings _emailSettings;

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
                List<string> emailList = DatabaseHelper.GetEmailsForLocation(property);

                if (emailList.Count == 0)
                {
                    Console.WriteLine($"No hay clientes suscritos que cumplan los filtros de la propiedad {property.Id}.");
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
                    Subject = $"Inmueble detectado: {property.Title}, {property.Location}",
                    IsBodyHtml = true
                };

                string htmlBody = $@"
                    <div style='text-align: center; margin-bottom: 20px;'>
                        <img src='cid:InmoAlertLogo' alt='InmoAlert Logo' style='max-width: 200px; height: auto;'/>
                    </div>
                    <h3>{property.Title}</h3>
                    <p><strong>ID:</strong> {property.Id}</p>
                    <p><strong>Ciudad:</strong> {property.Location}</p>
                    <p><strong>Provincia:</strong> {property.Province}</p>
                    <p><strong>Precio:</strong> {property.Price} {property.Currency}</p>
                    <p><strong>Tipo de vendedor:</strong> {Utils.GetVendorType(property)}</p>
                    <p><strong>Descripción:</strong> {property.Description}</p>
                    <p><a href='{property.Url}'>Ver en Idealista</a></p>";

                var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, null, MediaTypeNames.Text.Html);

                var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "inmoalert_logo.png");
                var logo = new LinkedResource(logoPath, MediaTypeNames.Image.Jpeg)
                {
                    ContentId = "InmoAlertLogo"
                };
                htmlView.LinkedResources.Add(logo);

                mailMessage.AlternateViews.Add(htmlView);

                foreach (var email in emailList)
                {
                    mailMessage.Bcc.Add(email);
                }

                await smtpClient.SendMailAsync(mailMessage);
                Console.WriteLine($"Correo enviado para la propiedad ID: {property.Id} a {emailList.Count} destinatarios.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al enviar el correo: {ex.Message}");
            }
        }
    }
}