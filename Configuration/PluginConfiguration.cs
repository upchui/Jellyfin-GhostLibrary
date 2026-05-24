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

    // ── Hidden / stealth libraries ────────────────────────────────────────────

    /// <summary>
    /// Gets or sets the GUIDs of the top-level libraries to hide completely.
    /// </summary>
    public string[] HiddenLibraryIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the GUIDs of libraries to show in "stealth" mode:
    /// the tile is kept but <c>ChildCount</c> is set to 0 so it appears empty.
    /// Content items belonging to stealth libraries are filtered from aggregated rows.
    /// </summary>
    public string[] StealthLibraryIds { get; set; } = Array.Empty<string>();

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
    /// belong to a hidden or stealth library are also removed from aggregated
    /// responses (Latest Added, Continue Watching, Next Up).
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
    /// Libraries without a rule are always hidden (when selected).
    /// </summary>
    public ScheduleRule[] ScheduleRules { get; set; } = Array.Empty<ScheduleRule>();

    // ── Webhook ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets the URL to POST to when the plugin configuration changes.
    /// Leave empty to disable. Useful for Home Assistant automations.
    /// </summary>
    public string WebhookUrl { get; set; } = string.Empty;

    // ── Logging ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets the maximum number of filter events kept in the in-memory log.
    /// Oldest entries are dropped when the buffer is full.
    /// </summary>
    public int MaxLogEntries { get; set; } = 100;
}

/// <summary>
/// Defines when a library is hidden. All non-empty conditions are ANDed together.
/// </summary>
public class ScheduleRule
{
    /// <summary>Gets or sets the library GUID this rule applies to.</summary>
    public string LibraryId { get; set; } = string.Empty;

    /// <summary>Gets or sets the first calendar date this rule is active ("yyyy-MM-dd"). Empty = no start limit.</summary>
    public string ActiveFrom { get; set; } = string.Empty;

    /// <summary>Gets or sets the last calendar date this rule is active ("yyyy-MM-dd"). Empty = no end limit (permanent).</summary>
    public string ActiveUntil { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets which days of the week this rule applies to.
    /// Index 0 = Monday … 6 = Sunday. All-false or empty array = every day.
    /// </summary>
    public bool[] ActiveDays { get; set; } = Array.Empty<bool>();

    /// <summary>Gets or sets the time-of-day to start hiding (e.g. "22:00"). Empty = all day.</summary>
    public string HideFrom { get; set; } = string.Empty;

    /// <summary>Gets or sets the time-of-day to stop hiding (e.g. "06:00"). Empty = all day.</summary>
    public string HideUntil { get; set; } = string.Empty;
}
