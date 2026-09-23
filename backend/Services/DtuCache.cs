using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Planner.Backend.Services;

// Bounded data cache. Striped locks also bound coordination memory; failed factories are never cached.
public sealed class DtuCache : IDisposable
{
    private readonly MemoryCache _cache;
    private readonly SemaphoreSlim[] _gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly TimeSpan _lifetime;

    public DtuCache(IOptions<DtuOptions> options)
    {
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.Value.CacheEntries });
        _lifetime = TimeSpan.FromMinutes(options.Value.CacheMinutes);
    }

    public bool TryGet<T>(string key, out T? value) => _cache.TryGetValue(key, out value);

    public void Set<T>(string key, T value, TimeSpan? lifetime = null) =>
        _cache.Set(key, value, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = lifetime ?? _lifetime });

    public async Task<T> WithLockAsync<T>(string key, Func<Task<T>> action)
    {
        var gate = _gates[(uint)StringComparer.Ordinal.GetHashCode(key) % _gates.Length];
        await gate.WaitAsync();
        try { return await action(); }
        finally { gate.Release(); }
    }

    public Task<T> GetAsync<T>(string key, Func<Task<T>> factory) => WithLockAsync(key, async () =>
    {
        if (TryGet<T>(key, out var value)) return value!;
        var loaded = await factory();
        Set(key, loaded);
        return loaded;
    });

    public void Dispose()
    {
        _cache.Dispose();
        foreach (var gate in _gates) gate.Dispose();
    }
}
