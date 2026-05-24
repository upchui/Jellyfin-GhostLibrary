using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.GhostLibrary.Services;

/// <summary>
/// A single filter event recorded by <see cref="Filters.GhostLibraryFilter"/>.
/// </summary>
public class FilterLogEntry
{
    /// <summary>Gets or sets the UTC timestamp of the event.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Gets or sets the request path (e.g. /Users/…/Views).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the client User-Agent header value.</summary>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>Gets or sets how many items were removed from the response.</summary>
    public int ItemsRemoved { get; set; }

    /// <summary>Gets or sets how many items remained after filtering.</summary>
    public int ItemsKept { get; set; }

    /// <summary>Gets or sets the reason the request was NOT filtered (null if it was filtered).</summary>
    public string? SkippedReason { get; set; }
}

/// <summary>
/// Thread-safe in-memory ring buffer that stores the last N filter events.
/// Registered as a singleton in DI; survives for the lifetime of the server process.
/// </summary>
public class FilterLog
{
    private const int Capacity = 100;

    private readonly Queue<FilterLogEntry> _entries = new();
    private readonly object _lock = new();

    /// <summary>Records a new filter event, dropping the oldest entry when full.</summary>
    public void Add(FilterLogEntry entry)
    {
        lock (_lock)
        {
            if (_entries.Count >= Capacity)
            {
                _entries.Dequeue();
            }

            _entries.Enqueue(entry);
        }
    }

    /// <summary>Returns all entries, newest first.</summary>
    public IReadOnlyList<FilterLogEntry> GetAll()
    {
        lock (_lock)
        {
            var list = new List<FilterLogEntry>(_entries);
            list.Reverse();
            return list;
        }
    }

    /// <summary>Clears all entries.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
}
