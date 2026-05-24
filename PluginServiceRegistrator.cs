using Jellyfin.Plugin.GhostLibrary.Filters;
using Jellyfin.Plugin.GhostLibrary.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.GhostLibrary;

/// <summary>
/// Registers GhostLibrary services with Jellyfin's dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost)
    {
        // Singletons — shared across all requests
        serviceCollection.AddSingleton<FilterLog>();
        serviceCollection.AddSingleton<WebhookService>();

        // Scoped filter — one instance per request, resolved from DI
        serviceCollection.AddScoped<GhostLibraryFilter>();

        // PostConfigure runs after ALL Jellyfin MVC configuration
        serviceCollection.PostConfigure<MvcOptions>(options =>
        {
            options.Filters.Add<GhostLibraryFilter>();
        });
    }
}
