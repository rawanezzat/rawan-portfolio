using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;

namespace Credit2.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<bool> SendEmailAsync(string toEmail, string subject, string body)
        {
            try
            {
                // Check if email is null or empty
                if (string.IsNullOrEmpty(toEmail))
                {
                    _logger.LogWarning("Recipient email is null or empty.");
                    return false;
                }

                var emailSettings = _config.GetSection("EmailSettings");

                // Validate configuration
                if (string.IsNullOrEmpty(emailSettings["SmtpServer"]) ||
                    string.IsNullOrEmpty(emailSettings["SenderEmail"]) ||
                    string.IsNullOrEmpty(emailSettings["Password"]))
                {
                    _logger.LogError("Email configuration is missing or incomplete.");
                    return false;
                }

                if (!int.TryParse(emailSettings["Port"], out int port))
                {
                    port = 587;
                }

                var senderEmail = emailSettings["SenderEmail"] ?? throw new ArgumentNullException("SenderEmail is required");
                var senderPassword = emailSettings["Password"] ?? throw new ArgumentNullException("Password is required");
                var senderName = emailSettings["SenderName"] ?? "Credit System";

                using (var client = new SmtpClient(emailSettings["SmtpServer"], port))
                {
                    client.Credentials = new NetworkCredential(senderEmail, senderPassword);
                    client.EnableSsl = true;
                    client.Timeout = 10000;

                    var mailMessage = new MailMessage
                    {
                        From = new MailAddress(senderEmail, senderName),
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = true
                    };

                    mailMessage.To.Add(toEmail.Trim());
                    await client.SendMailAsync(mailMessage);

                    _logger.LogInformation($"Email sent successfully to {toEmail}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending email to {toEmail}: {ex.Message}");
                return false;
            }
        }

    }
}