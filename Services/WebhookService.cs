using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.GhostLibrary.Services;

/// <summary>
/// Sends an HTTP POST to a configured webhook URL whenever the plugin
/// configuration changes.
/// </summary>
public class WebhookService
{
    private static readonly HttpClient Http = new();

    private readonly ILogger<WebhookService> _logger;

    /// <summary>Gets the singleton instance set by the DI container.</summary>
    public static WebhookService? Instance { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookService"/> class.
    /// </summary>
    public WebhookService(ILogger<WebhookService> logger)
    {
        _logger = logger;
        Instance = this;
    }

    /// <summary>
    /// Fires the webhook if a URL is configured. Does not throw on failure.
    /// </summary>
    public async Task NotifyAsync(string eventType, object payload)
    {
        var url = Plugin.Instance?.Configuration.WebhookUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            var body = JsonSerializer.Serialize(new
            {
                plugin    = "GhostLibrary",
                @event    = eventType,
                timestamp = DateTime.UtcNow,
                data      = payload
            });

            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await Http.PostAsync(url, content).ConfigureAwait(false);
            _logger.LogDebug(
                "GhostLibrary webhook sent to {Url}: {Status}", url, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GhostLibrary webhook failed for URL {Url}", url);
        }
    }
}
