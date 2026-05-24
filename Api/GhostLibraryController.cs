using System.Collections.Generic;
using Jellyfin.Plugin.GhostLibrary.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.GhostLibrary.Api;

/// <summary>
/// REST API for GhostLibrary plugin management.
/// All endpoints require the user to be authenticated as an administrator.
/// </summary>
[ApiController]
[Route("GhostLibrary")]
[Authorize(Policy = "RequiresElevation")]
public class GhostLibraryController : ControllerBase
{
    private readonly FilterLog _log;

    /// <summary>
    /// Initializes a new instance of the <see cref="GhostLibraryController"/> class.
    /// </summary>
    public GhostLibraryController(FilterLog log)
    {
        _log = log;
    }

    /// <summary>
    /// Returns the last 100 filter events (newest first).
    /// </summary>
    [HttpGet("Log")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<FilterLogEntry>> GetLog()
    {
        return Ok(_log.GetAll());
    }

    /// <summary>
    /// Clears the in-memory filter log.
    /// </summary>
    [HttpDelete("Log")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult ClearLog()
    {
        _log.Clear();
        return NoContent();
    }
}
