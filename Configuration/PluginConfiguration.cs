using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.GhostLibrary.Configuration;

/// <summary>
/// Plugin configuration for GhostLibrary.
/// Serialized to XML and stored in the Jellyfin plugin configuration directory.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    // ── Master switch ─────────────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets a value indicating whether the plugin is active.
    /// When false, all filtering is skipped without losing any configuration.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    // ── Hidden libraries ──────────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets the GUIDs of the top-level libraries to hide.
    /// </summary>
    public string[] HiddenLibraryIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the GUIDs of individual sub-folders or items to hide
    /// within otherwise visible libraries.
    /// </summary>
    public string[] HiddenFolderIds { get; set; } = Array.Empty<string>();

    // ── Behaviour toggles ─────────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets a value indicating whether administrators bypass the
    /// hidden list and always see every library.
    /// </summary>
    public bool VisibleToAdmins { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether individual media items that
    /// belong to a hidden library are also removed from aggregated responses
    /// (Latest Added, Continue Watching, Next Up).
    /// </summary>
    public bool FilterContentItems { get; set; } = true;

    // ── Client filter ─────────────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets a comma-separated list of User-Agent substrings that
    /// should be filtered. Leave empty to filter all clients.
    /// Example: "AndroidTV,Infuse"
    /// </summary>
    public string BlockedClients { get; set; } = string.Empty;

    // ── Schedule rules ────────────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets the per-library schedule rules.
    /// Each entry maps a library GUID to a <see cref="ScheduleRule"/>.
    /// Libraries without a rule are always hidden (when selected).
    /// </summary>
    public ScheduleRule[] ScheduleRules { get; set; } = Array.Empty<ScheduleRule>();
}

/// <summary>
/// Defines a time-window during which a library is hidden.
/// Outside this window the library is visible even if it is in the hidden list.
/// </summary>
public class ScheduleRule
{
    /// <summary>Gets or sets the library GUID this rule applies to.</summary>
    public string LibraryId { get; set; } = string.Empty;

    /// <summary>Gets or sets the time-of-day to start hiding (e.g. "06:00").</summary>
    public string HideFrom { get; set; } = "00:00";

    /// <summary>Gets or sets the time-of-day to stop hiding (e.g. "22:00").</summary>
    public string HideUntil { get; set; } = "23:59";
}
