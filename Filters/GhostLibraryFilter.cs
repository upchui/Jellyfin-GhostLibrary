using System;
using System.Collections.Generic;
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
/// Global ASP.NET Core action filter that removes the configured hidden library
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
        // Without this, Jellyfin may return a "304 Not Modified" response with
        // an empty body, bypassing the filter entirely and letting the client
        // use its stale (unfiltered) cached response.
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;
        if (path.EndsWith("/Views", StringComparison.OrdinalIgnoreCase))
        {
            context.HttpContext.Request.Headers.Remove("If-None-Match");
            context.HttpContext.Request.Headers.Remove("If-Modified-Since");
        }

        // Execute the controller action.
        var executedContext = await next().ConfigureAwait(false);

        // If the action threw an unhandled exception, bail out cleanly.
        if (executedContext.Exception is not null && !executedContext.ExceptionHandled)
        {
            return;
        }

        // We only care about ObjectResult payloads — anything else passes through unchanged.
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

        var hiddenName = config.HiddenLibraryName;
        var hiddenIdString = config.HiddenLibraryId;

        // Nothing configured → pass through unchanged.
        if (string.IsNullOrWhiteSpace(hiddenName) && string.IsNullOrWhiteSpace(hiddenIdString))
        {
            return;
        }

        // Parse the configured GUID once (may be null if not set or invalid).
        Guid? hiddenId = null;
        if (!string.IsNullOrWhiteSpace(hiddenIdString)
            && Guid.TryParse(hiddenIdString, out var parsedId))
        {
            hiddenId = parsedId;
        }

        // Build the filtered list.
        var filtered = new List<BaseItemDto>(queryResult.Items.Count);
        foreach (var item in queryResult.Items)
        {
            if (!ShouldHide(item, hiddenName, hiddenId))
            {
                filtered.Add(item);
            }
        }

        // Only mutate the response when at least one item was actually removed.
        if (filtered.Count == queryResult.Items.Count)
        {
            return;
        }

        // Update the result in-place (objectResult.Value is a reference type;
        // modifying its properties here is reflected when the result is serialized).
        queryResult.Items = filtered;
        queryResult.TotalRecordCount = filtered.Count;

        // Replace the original ETag (computed over the unfiltered set) with one
        // computed over the filtered set so clients do not treat our filtered
        // response as stale relative to the real content.
        executedContext.HttpContext.Response.Headers.Remove("ETag");
        executedContext.HttpContext.Response.Headers["ETag"] = ComputeEtag(filtered);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="item"/> is a top-level
    /// library folder that matches the configured hidden library.
    /// </summary>
    private static bool ShouldHide(BaseItemDto item, string hiddenName, Guid? hiddenId)
    {
        // Only CollectionFolder items represent library roots in Jellyfin.
        // This prevents accidentally hiding media items that share the same name.
        if (item.Type != BaseItemKind.CollectionFolder)
        {
            return false;
        }

        // ID match: most precise — use when configured.
        if (hiddenId.HasValue && item.Id == hiddenId.Value)
        {
            return true;
        }

        // Name match: case-insensitive ordinal comparison.
        if (!string.IsNullOrWhiteSpace(hiddenName)
            && item.Name is not null
            && item.Name.Equals(hiddenName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Computes a stable, quoted ETag string from the IDs of the filtered items.
    /// The "ghost-" prefix distinguishes it from Jellyfin's own ETags.
    /// </summary>
    private static string ComputeEtag(IReadOnlyList<BaseItemDto> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            sb.Append(item.Id.ToString("N"));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));

        // Use first 16 hex chars (64-bit entropy) — sufficient for cache validation.
        return $"\"ghost-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}\"";
    }
}
