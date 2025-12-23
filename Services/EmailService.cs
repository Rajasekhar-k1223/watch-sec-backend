using System.Net;
using System.Net.Mail;

namespace watch_sec_backend.Services;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body);
}

public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string body)
    {
        var smtpHost = _config["Email:Host"];
        var smtpPort = _config.GetValue<int>("Email:Port");
        var smtpUser = _config["Email:Username"];
        var smtpPass = _config["Email:Password"];
        var fromEmail = _config["Email:From"] ?? "noreply@watchsec.io";

        // If no config, simulate
        if (string.IsNullOrEmpty(smtpHost))
        {
            _logger.LogInformation("EMAIL SIMULATION to {To}: [{Subject}] {Body}", to, subject, body);
            return;
        }

        try
        {
            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                Credentials = new NetworkCredential(smtpUser, smtpPass),
                EnableSsl = true
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(fromEmail, "Watch Sec Platform"),
                Subject = subject,
                Body = body,
                IsBodyHtml = true,
            };
            mailMessage.To.Add(to);

            await client.SendMailAsync(mailMessage);
            _logger.LogInformation("Email sent successfully to {To}", to);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to send email to {To}: {Error}", to, ex.Message);
            // Don't throw, just log. Email failure shouldn't crash the app flow.
        }
    }
}
