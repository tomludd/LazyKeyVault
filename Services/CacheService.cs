using System.Collections.Concurrent;

namespace LazyKeyVault.Services;

/// <summary>
/// Thread-safe in-memory cache with TTL support.
/// </summary>
public class CacheService
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly TimeSpan _defaultTtl;

    public CacheService(TimeSpan? defaultTtl = null)
    {
        _defaultTtl = defaultTtl ?? TimeSpan.FromDays(365); // Cache forever (until refresh)
    }

    public T? Get<T>(string key)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > DateTime.UtcNow)
            {
                return (T)entry.Value;
            }
            // Expired, remove it
            _cache.TryRemove(key, out _);
        }
        return default;
    }

    public void Set<T>(string key, T value, TimeSpan? ttl = null)
    {
        _cache[key] = new CacheEntry
        {
            Value = value!,
            ExpiresAt = DateTime.UtcNow + (ttl ?? _defaultTtl)
        };
    }

    public bool TryGet<T>(string key, out T? value)
    {
        value = Get<T>(key);
        return value != null;
    }

    public void Invalidate(string key)
    {
        _cache.TryRemove(key, out _);
    }

    public void InvalidatePrefix(string prefix)
    {
        var keysToRemove = _cache.Keys.Where(k => k.StartsWith(prefix)).ToList();
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
    }

    public void Clear()
    {
        _cache.Clear();
    }

    private class CacheEntry
    {
        public required object Value { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
