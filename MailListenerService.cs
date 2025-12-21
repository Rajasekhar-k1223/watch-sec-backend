using System.Net;
using System.Net.Sockets;
using System.Text;
using MongoDB.Driver;

namespace watch_sec_backend;

public class MailListenerService : BackgroundService
{
    private readonly ILogger<MailListenerService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private TcpListener? _listener;

    public MailListenerService(ILogger<MailListenerService> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Listen on port 2525 (Non-privileged SMTP)
            _listener = new TcpListener(IPAddress.Any, 2525);
            _listener.Start();
            _logger.LogInformation("SMTP Interceptor started on port 2525");

            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleClientAsync(client);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("SMTP Listener failed: {Error}", ex.Message);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.ASCII))
        using (var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true })
        {
            try
            {
                // Simple SMTP Handshake Simulation
                await writer.WriteLineAsync("220 WatchSec DLP SMTP Service Ready");

                string? line;
                var log = new MailLog();
                var bodyBuilder = new StringBuilder();
                bool readingBody = false;

                while ((line = await reader.ReadLineAsync()) != null)
                {
                    // _logger.LogInformation("SMTP Rx: {Line}", line);

                    if (readingBody)
                    {
                        if (line == ".")
                        {
                            readingBody = false;
                            log.BodyPreview = bodyBuilder.ToString().Substring(0, Math.Min(bodyBuilder.Length, 100)) + "...";
                            await writer.WriteLineAsync("250 OK Message accepted for delivery");
                            
                            // Save to DB
                            await SaveLogAsync(log);
                        }
                        else
                        {
                            bodyBuilder.AppendLine(line);
                        }
                    }
                    else if (line.StartsWith("HELO") || line.StartsWith("EHLO"))
                    {
                        await writer.WriteLineAsync("250 Hello");
                    }
                    else if (line.StartsWith("MAIL FROM:"))
                    {
                        log.Sender = line.Substring(10).Trim('<', '>', ' ');
                        await writer.WriteLineAsync("250 OK");
                    }
                    else if (line.StartsWith("RCPT TO:"))
                    {
                        log.Recipient = line.Substring(8).Trim('<', '>', ' ');
                        await writer.WriteLineAsync("250 OK");
                    }
                    else if (line.StartsWith("DATA"))
                    {
                        await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                        readingBody = true;
                    }
                    else if (line.StartsWith("QUIT"))
                    {
                        await writer.WriteLineAsync("221 Bye");
                        break;
                    }
                    else if (line.StartsWith("Subject:"))
                    {
                         // Header parsing (simplified, usually inside DATA block but for demo we catch it here if sent early or inside body loop logic needs refinement for real SMTP)
                         // For this simple demo, we assume headers come after DATA but before body content
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error handling SMTP client: {Error}", ex.Message);
            }
        }
    }

    private async Task SaveLogAsync(MailLog log)
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
            var db = mongo.GetDatabase("watchsec");
            var collection = db.GetCollection<MailLog>("mail_logs");
            await collection.InsertOneAsync(log);
            _logger.LogInformation("Intercepted Email from {Sender} to {Recipient}", log.Sender, log.Recipient);
        }
    }
}
