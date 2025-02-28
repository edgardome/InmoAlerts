using System.Net.Mail;
using System.Net;
using static Idealista.Models.Models;
using System.Net.Mime;

namespace Idealista.Helpers
{
    public class EmailHelper
    {
        private const string ConnectionString = "Data Source=properties.db;Version=3;";

        public static void SendPropertyEmail(Property property)
        {
            try
            {
                // Obtener lista de correos desde la base de datos según la ciudad de la propiedad
                List<string> emailList = DatabaseHelper.GetEmailsForCity(property.Location);

                if (emailList.Count == 0)
                {
                    Console.WriteLine("No hay clientes suscritos a esta ciudad.");
                    return;
                }

                // Configuración del cliente SMTP de Gmail
                using var smtpClient = new SmtpClient("smtp.gmail.com")
                {
                    Port = 587,
                    Credentials = new NetworkCredential("alertasinmueblesbeta@gmail.com", "efef jtmq dytl grka"),
                    EnableSsl = true
                };

                // Configuración del correo
                var mailMessage = new MailMessage
                {
                    From = new MailAddress("alertasinmueblesbeta@gmail.com"),
                    Subject = $"Nueva Propiedad Detectada: {property.Title}",
                    IsBodyHtml = true
                };

                // Crear el cuerpo del correo con la imagen
                string htmlBody = $@"
                <div style='text-align: center; margin-bottom: 20px;'>
                    <img src='cid:InmoAlertLogo' alt='InmoAlert Logo' style='max-width: 200px; height: auto;'/>
                </div>
                <h3>{property.Title}</h3>
                <p><strong>ID:</strong> {property.Id}</p>
                <p><strong>Ubicación:</strong> {property.Location}</p>
                <p><strong>Precio:</strong> {property.Price} {property.Currency}</p>
                <p><strong>Descripción:</strong> {property.Description}</p>
                <p><a href='{property.Url}'>Ver en Idealista</a></p>";

                // Crear vista alternativa para HTML
                var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, null, MediaTypeNames.Text.Html);

                // Añadir la imagen como recurso vinculado
                var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "inmoalert_logo.png");
                var logo = new LinkedResource(logoPath, MediaTypeNames.Image.Jpeg)
                {
                    ContentId = "InmoAlertLogo"
                };
                htmlView.LinkedResources.Add(logo);

                // Añadir la vista alternativa al mensaje
                mailMessage.AlternateViews.Add(htmlView);

                // Añadir destinatarios desde la base de datos
                foreach (var email in emailList)
                {
                    mailMessage.Bcc.Add(email);
                }

                // Enviar el correo
                smtpClient.Send(mailMessage);
                Console.WriteLine($"Correo enviado para la propiedad ID: {property.Id} a {emailList.Count} destinatarios.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al enviar el correo: {ex.Message}");
            }
        }
    }
}