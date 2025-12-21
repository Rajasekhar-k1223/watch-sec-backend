using System.Net;
using System.Net.Sockets;
using System.Text;

namespace watch_sec_backend;

public class IcapService : BackgroundService
{
    private readonly ILogger<IcapService> _logger;
    private TcpListener? _listener;

    public IcapService(ILogger<IcapService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Listen on port 1344 (Standard ICAP)
            _listener = new TcpListener(IPAddress.Any, 1344);
            _listener.Start();
            _logger.LogInformation("ICAP Server started on port 1344");

            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleClientAsync(client);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("ICAP Server failed: {Error}", ex.Message);
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
                // Basic ICAP REQMOD/RESPMOD Handling
                // This is a minimal implementation to acknowledge requests
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrEmpty(line)) break; // End of headers

                    if (line.StartsWith("OPTIONS"))
                    {
                        await writer.WriteLineAsync("ICAP/1.0 200 OK");
                        await writer.WriteLineAsync("Methods: RESPMOD, REQMOD");
                        await writer.WriteLineAsync("Service: WatchSec DLP 1.0");
                        await writer.WriteLineAsync("ISTag: \"WatchSec-DLP-v1\"");
                        await writer.WriteLineAsync();
                        return;
                    }
                    else if (line.StartsWith("REQMOD") || line.StartsWith("RESPMOD"))
                    {
                        // Default: Allow everything (204 No Content means "No modification needed")
                        await writer.WriteLineAsync("ICAP/1.0 204 No Content");
                        await writer.WriteLineAsync("ISTag: \"WatchSec-DLP-v1\"");
                        await writer.WriteLineAsync();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error handling ICAP client: {Error}", ex.Message);
            }
        }
    }
}
