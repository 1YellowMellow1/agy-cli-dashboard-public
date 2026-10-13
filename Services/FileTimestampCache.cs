using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace AgyCliDashboard.Services;

public static class FileTimestampCache
{
    private class CacheEntry
    {
        public DateTime LastWriteTimeUtc { get; set; }
        public object? Data { get; set; }
    }

    private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns cached data if the UTC write timestamp is unchanged.
    /// Otherwise calls the async load function and updates the cache.
    /// </summary>
    public static async Task<T?> GetOrLoadAsync<T>(string filePath, Func<string, Task<T?>> loadFunc) where T : class
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            _cache.TryRemove(filePath ?? string.Empty, out _);
            return null;
        }

        DateTime currentLastWrite = File.GetLastWriteTimeUtc(filePath);

        if (_cache.TryGetValue(filePath, out var entry))
        {
            if (entry.LastWriteTimeUtc == currentLastWrite && entry.Data is T cachedTyped)
            {
                return cachedTyped;
            }
        }

        T? newData = await loadFunc(filePath);
        if (newData != null)
        {
            _cache[filePath] = new CacheEntry
            {
                LastWriteTimeUtc = currentLastWrite,
                Data = newData
            };
        }

        return newData;
    }

    /// <summary>
    /// Synchronous overload for synchronous workflows.
    /// </summary>
    public static T? GetOrLoadSync<T>(string filePath, Func<string, T?> loadFunc) where T : class
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            _cache.TryRemove(filePath ?? string.Empty, out _);
            return null;
        }

        DateTime currentLastWrite = File.GetLastWriteTimeUtc(filePath);

        if (_cache.TryGetValue(filePath, out var entry))
        {
            if (entry.LastWriteTimeUtc == currentLastWrite && entry.Data is T cachedTyped)
            {
                return cachedTyped;
            }
        }

        T? newData = loadFunc(filePath);
        if (newData != null)
        {
            _cache[filePath] = new CacheEntry
            {
                LastWriteTimeUtc = currentLastWrite,
                Data = newData
            };
        }

        return newData;
    }

    /// <summary>
    /// Invalidates the cache entry for a file (e.g., after atomic writing).
    /// </summary>
    public static void Invalidate(string filePath)
    {
        if (!string.IsNullOrEmpty(filePath))
        {
            _cache.TryRemove(filePath, out _);
        }
    }

    /// <summary>
    /// Clears the entire cache.
    /// </summary>
    public static void Clear()
    {
        _cache.Clear();
    }
}
