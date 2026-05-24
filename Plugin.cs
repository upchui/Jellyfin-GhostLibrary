using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.GhostLibrary.Configuration;
using Jellyfin.Plugin.GhostLibrary.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.GhostLibrary;

/// <summary>
/// GhostLibrary plugin — hides configured media libraries from Jellyfin client
/// API responses while keeping them fully accessible to other plugins and the filesystem.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        ConfigurationChanged += OnConfigurationChanged;
    }

    /// <inheritdoc />
    public override string Name => "GhostLibrary";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("8a3b2c7d-f1e4-4a9b-b6d3-1c2e5f8a0d4e");

    /// <summary>Gets the current active plugin instance.</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.ConfigurationPage.html",
                    GetType().Namespace)
            }
        };
    }

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration e)
    {
        // Fire webhook asynchronously — do not block the save path.
        var webhook = WebhookService.Instance;
        if (webhook is not null && e is PluginConfiguration cfg)
        {
            _ = webhook.NotifyAsync("configuration_changed", new
            {
                hiddenLibraryCount  = cfg.HiddenLibraryIds.Length,
                stealthLibraryCount = cfg.StealthLibraryIds.Length,
                isEnabled           = cfg.IsEnabled
            });
        }
    }
}
