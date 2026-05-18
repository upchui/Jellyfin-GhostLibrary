using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.GhostLibrary.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.GhostLibrary;

/// <summary>
/// GhostLibrary plugin — hides a configured media library from Jellyfin client API responses
/// while keeping the library fully accessible to other plugins and the filesystem.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "GhostLibrary";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("8a3b2c7d-f1e4-4a9b-b6d3-1c2e5f8a0d4e");

    /// <summary>
    /// Gets the current active plugin instance.
    /// Set during construction; used by <see cref="Filters.GhostLibraryFilter"/> to read configuration.
    /// </summary>
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
}
