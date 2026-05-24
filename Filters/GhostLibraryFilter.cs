using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.GhostLibrary.Filters;

/// <summary>
/// Global ASP.NET Core action filter that removes configured hidden libraries —
/// and optionally their content — from <see cref="QueryResult{T}"/> API responses.
///
/// Intercepted endpoints:
/// <list type="bullet">
///   <item><description>GET /Users/{userId}/Views — library tiles</description></item>
///   <item><description>GET /Items — generic item queries (Latest, Resume, Next Up, …)</description></item>
/// </list>
///
/// Internal <c>ILibraryManager</c> access (Cinema Mode etc.) is never affected.
/// </summary>
public class GhostLibraryFilter : IAsyncActionFilter
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="GhostLibraryFilter"/> class.
    /// </summary>
    public GhostLibraryFilter(ILibraryManager libraryManager, IUserManager userManager)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
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

        // ── Admin bypass ──────────────────────────────────────────────────────
        // When enabled, admins always see every library regardless of the hidden list.
        if (config.VisibleToAdmins)
        {
            var userIdValue = context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdValue is not null && Guid.TryParse(userIdValue, out var requestingUserId))
            {
                var user = _userManager.GetUserById(requestingUserId);
                if (user?.HasPermission(PermissionKind.IsAdministrator) == true)
                {
                    return;
                }
            }
        }

        // ── Build hidden-ID lookup ────────────────────────────────────────────
        var hiddenIds = new HashSet<Guid>(
            (config.HiddenLibraryIds ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out _))
                .Select(s => Guid.Parse(s)));

        if (hiddenIds.Count == 0)
        {
            return;
        }

        // ── Filter items ──────────────────────────────────────────────────────
        var filtered = new List<BaseItemDto>(queryResult.Items.Count);
        foreach (var item in queryResult.Items)
        {
            if (!ShouldHide(item, hiddenIds, config.FilterContentItems))
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

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="item"/> should be
    /// removed from the response.
    /// </summary>
    private bool ShouldHide(BaseItemDto item, HashSet<Guid> hiddenIds, bool filterContent)
    {
        // Library tile — direct ID match.
        if (item.Type == BaseItemKind.CollectionFolder)
        {
            return hiddenIds.Contains(item.Id);
        }

        // Media item — check if it lives inside a hidden library.
        if (!filterContent)
        {
            return false;
        }

        return IsInsideHiddenLibrary(item.Id, hiddenIds);
    }

    /// <summary>
    /// Walks the parent chain of <paramref name="itemId"/> until a root
    /// CollectionFolder is found. Returns <see langword="true"/> if that
    /// root is in <paramref name="hiddenIds"/>.
    /// ILibraryManager caches items in memory, so the traversal is fast.
    /// </summary>
    private bool IsInsideHiddenLibrary(Guid itemId, HashSet<Guid> hiddenIds)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return false;
        }

        var parentId = item.ParentId;
        while (parentId != Guid.Empty)
        {
            if (hiddenIds.Contains(parentId))
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
