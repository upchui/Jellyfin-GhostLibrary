using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.GhostLibrary.Configuration;

/// <summary>
/// Plugin configuration for GhostLibrary.
/// Serialized to XML and stored in the Jellyfin plugin configuration directory.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the display name of the library to hide (case-insensitive).
    /// Matched against <c>BaseItemDto.Name</c> in API responses.
    /// Leave empty to match by <see cref="HiddenLibraryId"/> only.
    /// </summary>
    public string HiddenLibraryName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the GUID of the library to hide (optional).
    /// More precise than name matching — survives library renames.
    /// Find the ID in the Jellyfin dashboard URL when browsing the library.
    /// Leave empty to match by <see cref="HiddenLibraryName"/> only.
    /// </summary>
    public string HiddenLibraryId { get; set; } = string.Empty;
}
