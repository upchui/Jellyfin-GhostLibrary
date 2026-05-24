using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.GhostLibrary.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.GhostLibrary.Filters;

/// <summary>
/// Global ASP.NET Core action filter that removes configured hidden libraries —
/// and optionally their content — from <see cref="QueryResult{T}"/> API responses.
/// </summary>
public class GhostLibraryFilter : IAsyncActionFilter
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="GhostLibraryFilter"/> class.
    /// </summary>
    public GhostLibraryFilter(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        // Strip conditional GET headers on /Views so Jellyfin cannot short-circuit
        // with a 304 that bypasses this filter.
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;
        if (path.EndsWith("/Views", StringComparison.OrdinalIgnoreCase))
        {
            context.HttpContext.Request.Headers.Remove("If-None-Match");
            context.HttpContext.Request.Headers.Remove("If-Modified-Since");
        }

        var executedContext = await next().ConfigureAwait(false);

        if (executedContext.Exception is not null && !executedContext.ExceptionHandled)
        {
            return;
        }

        if (executedContext.Result is not ObjectResult objectResult)
        {
            return;
        }

        if (objectResult.Value is not QueryResult<BaseItemDto> queryResult)
        {
            return;
        }

        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            return;
        }

        // ── Master switch ─────────────────────────────────────────────────────
        if (!config.IsEnabled)
        {
            return;
        }

        // ── Client filter ─────────────────────────────────────────────────────
        // If BlockedClients is non-empty, only filter requests whose User-Agent
        // contains at least one of the configured substrings (case-insensitive).
        if (!string.IsNullOrWhiteSpace(config.BlockedClients))
        {
            var userAgent = context.HttpContext.Request.Headers.UserAgent.ToString();
            var tokens = config.BlockedClients
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var isBlocked = tokens.Any(t =>
                userAgent.Contains(t, StringComparison.OrdinalIgnoreCase));
            if (!isBlocked)
            {
                return;
            }
        }

        // ── Admin bypass ──────────────────────────────────────────────────────
        if (config.VisibleToAdmins
            && context.HttpContext.User.HasClaim(ClaimTypes.Role, "Administrator"))
        {
            return;
        }

        // ── Build hidden-ID sets ──────────────────────────────────────────────
        var now = DateTime.Now.TimeOfDay;

        // Respect per-library schedule: remove IDs whose hide-window does NOT
        // cover the current time (i.e. the library should be visible right now).
        var scheduleMap = (config.ScheduleRules ?? Array.Empty<ScheduleRule>())
            .Where(r => !string.IsNullOrWhiteSpace(r.LibraryId)
                        && TimeSpan.TryParse(r.HideFrom,  out _)
                        && TimeSpan.TryParse(r.HideUntil, out _))
            .ToDictionary(r => r.LibraryId, r => r);

        var hiddenLibraryIds = new HashSet<Guid>(
            (config.HiddenLibraryIds ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out _))
                .Where(s => IsActiveSchedule(s, scheduleMap, now))
                .Select(s => Guid.Parse(s)));

        var hiddenFolderIds = new HashSet<Guid>(
            (config.HiddenFolderIds ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out _))
                .Select(s => Guid.Parse(s)));

        if (hiddenLibraryIds.Count == 0 && hiddenFolderIds.Count == 0)
        {
            return;
        }

        // ── Filter items ──────────────────────────────────────────────────────
        var filtered = new List<BaseItemDto>(queryResult.Items.Count);
        foreach (var item in queryResult.Items)
        {
            if (!ShouldHide(item, hiddenLibraryIds, hiddenFolderIds, config.FilterContentItems))
            {
                filtered.Add(item);
            }
        }

        if (filtered.Count == queryResult.Items.Count)
        {
            return;
        }

        queryResult.Items = filtered;
        queryResult.TotalRecordCount = filtered.Count;

        executedContext.HttpContext.Response.Headers.Remove("ETag");
        executedContext.HttpContext.Response.Headers["ETag"] = ComputeEtag(filtered);
    }

    // ── Schedule helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when the library should currently be hidden.
    /// A library with no rule is always hidden. A library whose rule does not cover
    /// <paramref name="now"/> is visible (not hidden).
    /// </summary>
    private static bool IsActiveSchedule(
        string libraryId,
        Dictionary<string, ScheduleRule> scheduleMap,
        TimeSpan now)
    {
        if (!scheduleMap.TryGetValue(libraryId, out var rule))
        {
            return true; // no rule → always hide
        }

        TimeSpan.TryParse(rule.HideFrom,  out var from);
        TimeSpan.TryParse(rule.HideUntil, out var until);

        // Handle overnight windows (e.g. 22:00 – 06:00)
        if (from <= until)
        {
            return now >= from && now <= until;
        }
        else
        {
            return now >= from || now <= until;
        }
    }

    // ── Item filter ───────────────────────────────────────────────────────────

    private bool ShouldHide(
        BaseItemDto item,
        HashSet<Guid> hiddenLibraryIds,
        HashSet<Guid> hiddenFolderIds,
        bool filterContent)
    {
        // Top-level library tile
        if (item.Type == BaseItemKind.CollectionFolder)
        {
            return hiddenLibraryIds.Contains(item.Id);
        }

        // Sub-folder hidden directly by ID
        if (hiddenFolderIds.Contains(item.Id))
        {
            return true;
        }

        // Media item inside a hidden library or hidden sub-folder
        if (filterContent && (hiddenLibraryIds.Count > 0 || hiddenFolderIds.Count > 0))
        {
            return IsInsideHiddenFolder(item.Id, hiddenLibraryIds, hiddenFolderIds);
        }

        return false;
    }

    /// <summary>
    /// Walks the parent chain. Returns <see langword="true"/> if any ancestor
    /// is in the hidden-library or hidden-folder set.
    /// </summary>
    private bool IsInsideHiddenFolder(
        Guid itemId,
        HashSet<Guid> hiddenLibraryIds,
        HashSet<Guid> hiddenFolderIds)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return false;
        }

        var parentId = item.ParentId;
        while (parentId != Guid.Empty)
        {
            if (hiddenLibraryIds.Contains(parentId) || hiddenFolderIds.Contains(parentId))
            {
                return true;
            }

            var parent = _libraryManager.GetItemById(parentId);
            if (parent is null)
            {
                break;
            }

            parentId = parent.ParentId;
        }

        return false;
    }

    // ── ETag ──────────────────────────────────────────────────────────────────

    private static string ComputeEtag(IReadOnlyList<BaseItemDto> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            sb.Append(item.Id.ToString("N"));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return $"\"ghost-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}\"";
    }
}
