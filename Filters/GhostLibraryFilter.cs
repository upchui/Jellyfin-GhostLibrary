using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.GhostLibrary.Filters;

/// <summary>
/// Global ASP.NET Core action filter that removes all configured hidden libraries
/// from <see cref="QueryResult{T}"/> responses before they are serialized and
/// sent to clients.
///
/// This filter intercepts the two endpoints that expose library lists:
/// <list type="bullet">
///   <item><description>GET /Users/{userId}/Views</description></item>
///   <item><description>GET /Items (when returning CollectionFolder items)</description></item>
/// </list>
///
/// Internal access via <c>ILibraryManager</c> (used by Cinema Mode and similar
/// plugins) is never affected — only outgoing HTTP responses are modified.
/// </summary>
public class GhostLibraryFilter : IAsyncActionFilter
{
    /// <inheritdoc />
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        // Pre-execution: strip conditional GET headers for /Views requests.
        // Without this, Jellyfin may return a "304 Not Modified" with an empty body,
        // bypassing the filter and letting the client use its stale cached response.
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

        // Build a HashSet<Guid> for O(1) lookups; skip invalid/empty entries.
        var hiddenIds = new HashSet<Guid>(
            (config.HiddenLibraryIds ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out _))
            .Select(s => Guid.Parse(s)));

        if (hiddenIds.Count == 0)
        {
            return;
        }

        var filtered = new List<BaseItemDto>(queryResult.Items.Count);
        foreach (var item in queryResult.Items)
        {
            if (!ShouldHide(item, hiddenIds))
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

        // Replace the ETag so clients don't serve a stale (unfiltered) cached response.
        executedContext.HttpContext.Response.Headers.Remove("ETag");
        executedContext.HttpContext.Response.Headers["ETag"] = ComputeEtag(filtered);
    }

    private static bool ShouldHide(BaseItemDto item, HashSet<Guid> hiddenIds)
    {
        // CollectionFolder items are the library roots shown in the client.
        if (item.Type != BaseItemKind.CollectionFolder)
        {
            return false;
        }

        return hiddenIds.Contains(item.Id);
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
