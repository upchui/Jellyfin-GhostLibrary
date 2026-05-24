using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.GhostLibrary.Configuration;
using Jellyfin.Plugin.GhostLibrary.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.GhostLibrary.Filters;

/// <summary>
/// Global ASP.NET Core action filter that removes or empties configured libraries
/// from <see cref="QueryResult{T}"/> API responses before they reach the client.
/// </summary>
public class GhostLibraryFilter : IAsyncActionFilter
{
    private readonly ILibraryManager _libraryManager;
    private readonly FilterLog _log;

    /// <summary>
    /// Initializes a new instance of the <see cref="GhostLibraryFilter"/> class.
    /// </summary>
    public GhostLibraryFilter(ILibraryManager libraryManager, FilterLog log)
    {
        _libraryManager = libraryManager;
        _log = log;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var path      = context.HttpContext.Request.Path.Value ?? string.Empty;
        var userAgent = context.HttpContext.Request.Headers.UserAgent.ToString();

        // Never filter intro/theme requests — these must always pass through.
        if (path.EndsWith("/Intros", StringComparison.OrdinalIgnoreCase))
        {
            await next().ConfigureAwait(false);
            return;
        }

        // Strip conditional GET headers on /Views so Jellyfin cannot return 304
        // which would bypass this filter entirely.
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
            _log.Add(new FilterLogEntry
            {
                Timestamp     = DateTime.UtcNow,
                Path          = path,
                UserAgent     = userAgent,
                ItemsRemoved  = 0,
                ItemsKept     = queryResult.Items.Count,
                SkippedReason = "Plugin disabled"
            });
            return;
        }

        // ── Client filter ─────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(config.BlockedClients))
        {
            var tokens = config.BlockedClients
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var isBlocked = tokens.Any(t =>
                userAgent.Contains(t, StringComparison.OrdinalIgnoreCase));
            if (!isBlocked)
            {
                _log.Add(new FilterLogEntry
                {
                    Timestamp     = DateTime.UtcNow,
                    Path          = path,
                    UserAgent     = userAgent,
                    ItemsRemoved  = 0,
                    ItemsKept     = queryResult.Items.Count,
                    SkippedReason = "Client not in blocked list"
                });
                return;
            }
        }

        // ── Admin bypass ──────────────────────────────────────────────────────
        if (config.VisibleToAdmins
            && context.HttpContext.User.HasClaim(ClaimTypes.Role, "Administrator"))
        {
            _log.Add(new FilterLogEntry
            {
                Timestamp     = DateTime.UtcNow,
                Path          = path,
                UserAgent     = userAgent,
                ItemsRemoved  = 0,
                ItemsKept     = queryResult.Items.Count,
                SkippedReason = "Admin user"
            });
            return;
        }

        // ── Build ID sets ─────────────────────────────────────────────────────
        var now = DateTime.Now.TimeOfDay;

        var scheduleMap = (config.ScheduleRules ?? Array.Empty<ScheduleRule>())
            .Where(r => !string.IsNullOrWhiteSpace(r.LibraryId))
            .ToDictionary(r => r.LibraryId, r => r);

        var hiddenIds = new HashSet<Guid>(
            (config.HiddenLibraryIds ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out _))
                .Where(s => IsActiveSchedule(s, scheduleMap, now))
                .Select(s => Guid.Parse(s)));

        var stealthIds = new HashSet<Guid>(
            (config.StealthLibraryIds ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out _))
                .Select(s => Guid.Parse(s)));

        var hiddenFolderIds = new HashSet<Guid>(
            (config.HiddenFolderIds ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out _))
                .Select(s => Guid.Parse(s)));

        if (hiddenIds.Count == 0 && stealthIds.Count == 0 && hiddenFolderIds.Count == 0)
        {
            return;
        }

        // ── Filter / mutate items ─────────────────────────────────────────────
        // Always filter content on /Similar endpoints, regardless of the global toggle.
        var isSimilarEndpoint = path.Contains("/Similar", StringComparison.OrdinalIgnoreCase);
        var filterContent = config.FilterContentItems || isSimilarEndpoint;

        var filtered = new List<BaseItemDto>(queryResult.Items.Count);
        foreach (var item in queryResult.Items)
        {
            var action = GetAction(item, hiddenIds, stealthIds, hiddenFolderIds,
                                   filterContent);
            switch (action)
            {
                case ItemAction.Remove:
                    break; // skip
                case ItemAction.Stealth:
                    item.ChildCount = 0;
                    filtered.Add(item);
                    break;
                default:
                    filtered.Add(item);
                    break;
            }
        }

        var removed = queryResult.Items.Count - filtered.Count;

        _log.Add(new FilterLogEntry
        {
            Timestamp    = DateTime.UtcNow,
            Path         = path,
            UserAgent    = userAgent,
            ItemsRemoved = removed,
            ItemsKept    = filtered.Count
        });

        if (removed == 0)
        {
            return;
        }

        queryResult.Items = filtered;
        queryResult.TotalRecordCount = filtered.Count;

        executedContext.HttpContext.Response.Headers.Remove("ETag");
        executedContext.HttpContext.Response.Headers["ETag"] = ComputeEtag(filtered);
    }

    // ── Item action ───────────────────────────────────────────────────────────

    private enum ItemAction { Keep, Remove, Stealth }

    private ItemAction GetAction(
        BaseItemDto item,
        HashSet<Guid> hiddenIds,
        HashSet<Guid> stealthIds,
        HashSet<Guid> hiddenFolderIds,
        bool filterContent)
    {
        if (item.Type == BaseItemKind.CollectionFolder)
        {
            if (hiddenIds.Contains(item.Id))  return ItemAction.Remove;
            if (stealthIds.Contains(item.Id)) return ItemAction.Stealth;
            return ItemAction.Keep;
        }

        // Sub-folder hidden directly by ID
        if (hiddenFolderIds.Contains(item.Id))
        {
            return ItemAction.Remove;
        }

        // Media item inside a hidden/stealth library
        if (filterContent && (hiddenIds.Count > 0 || stealthIds.Count > 0 || hiddenFolderIds.Count > 0))
        {
            return GetContentAction(item.Id, hiddenIds, stealthIds, hiddenFolderIds);
        }

        return ItemAction.Keep;
    }

    private ItemAction GetContentAction(
        Guid itemId,
        HashSet<Guid> hiddenIds,
        HashSet<Guid> stealthIds,
        HashSet<Guid> hiddenFolderIds)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null) return ItemAction.Keep;

        // Use GetCollectionFolders — the authoritative API for mapping any item to its
        // top-level library (CollectionFolder). Walking ParentId alone is unreliable
        // because CollectionFolder nodes are virtual and may not appear in the chain.
        foreach (var folder in _libraryManager.GetCollectionFolders(item))
        {
            if (hiddenIds.Contains(folder.Id))  return ItemAction.Remove;
            if (stealthIds.Contains(folder.Id)) return ItemAction.Remove;
        }

        // Also walk parent chain for sub-folder hiding (HiddenFolderIds).
        if (hiddenFolderIds.Count > 0)
        {
            var parentId = item.ParentId;
            while (parentId != Guid.Empty)
            {
                if (hiddenFolderIds.Contains(parentId))
                    return ItemAction.Remove;

                var parent = _libraryManager.GetItemById(parentId);
                if (parent is null) break;
                parentId = parent.ParentId;
            }
        }

        return ItemAction.Keep;
    }

    // ── Schedule helper ───────────────────────────────────────────────────────

    private static bool IsActiveSchedule(
        string libraryId,
        Dictionary<string, ScheduleRule> scheduleMap,
        TimeSpan now)
    {
        if (!scheduleMap.TryGetValue(libraryId, out var rule)) return true;

        var today = DateTime.Now;

        // ── Date range ────────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(rule.ActiveFrom)
            && DateTime.TryParse(rule.ActiveFrom, out var activeFrom)
            && today.Date < activeFrom.Date)
            return false;

        if (!string.IsNullOrEmpty(rule.ActiveUntil)
            && DateTime.TryParse(rule.ActiveUntil, out var activeUntil)
            && today.Date > activeUntil.Date)
            return false;

        // ── Day of week ───────────────────────────────────────────────────────
        // ActiveDays[0]=Mon … [6]=Sun; DayOfWeek: Sun=0, Mon=1 … Sat=6
        var days = rule.ActiveDays;
        if (days is { Length: 7 } && days.Any(d => d))
        {
            var dayIndex = ((int)today.DayOfWeek + 6) % 7;
            if (!days[dayIndex]) return false;
        }

        // ── Daily time window ─────────────────────────────────────────────────
        if (!TimeSpan.TryParse(rule.HideFrom,  out var from)
            || !TimeSpan.TryParse(rule.HideUntil, out var until))
            return true; // no time restriction → always hide

        return from <= until
            ? now >= from && now <= until
            : now >= from || now <= until; // overnight span
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
