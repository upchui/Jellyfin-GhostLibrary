using Jellyfin.Plugin.GhostLibrary.Filters;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.GhostLibrary;

/// <summary>
/// Registers GhostLibrary services with Jellyfin's dependency injection container.
/// Jellyfin discovers this class via reflection at startup and calls
/// <see cref="RegisterServices"/> before the HTTP pipeline is active.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost)
    {
        // Register the filter as scoped so MVC can resolve it from DI per-request
        // when using typed filter registration (Filters.Add<T>()).
        serviceCollection.AddScoped<GhostLibraryFilter>();

        // PostConfigure runs after ALL Configure<MvcOptions> calls — including
        // Jellyfin's own AddJellyfinApi() — so the filter is always appended last.
        serviceCollection.PostConfigure<MvcOptions>(options =>
        {
            options.Filters.Add<GhostLibraryFilter>();
        });
    }
}
