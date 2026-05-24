using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.GhostLibrary.Configuration;

/// <summary>
/// Plugin configuration for GhostLibrary.
/// Serialized to XML and stored in the Jellyfin plugin configuration directory.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the GUIDs of the libraries to hide.
    /// Each entry is a string representation of a <see cref="Guid"/>.
    /// </summary>
    public string[] HiddenLibraryIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets a value indicating whether administrators always see all
    /// libraries regardless of the hidden list.
    /// </summary>
    public bool VisibleToAdmins { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether individual media items that
    /// belong to a hidden library are also removed from aggregated responses
    /// such as "Latest Added", "Continue Watching", and "Next Up".
    /// </summary>
    public bool FilterContentItems { get; set; } = true;
}
