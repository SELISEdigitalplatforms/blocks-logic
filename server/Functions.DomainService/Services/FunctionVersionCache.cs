using System.Collections.Concurrent;
using Functions.DomainService.Entities;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// Version documents kept in memory by version id, for the invocation hot path. Safe because a
    /// version is never changed after it is created (the repository only creates and deletes), and
    /// a new deploy gets a new id — the caller still reads the function first, so the active id it
    /// asks for is always current. A version holds secret <i>references</i> only, never values, so
    /// the no-secret-cache rule holds. Misses are never kept: an id that is not found is read
    /// again next time. Entries expire after <see cref="Lifetime"/>; past <see cref="MaxEntries"/>
    /// the whole map is dropped (cheap, and a refill is one Mongo read per live version).
    /// Callers must not change the returned entity: it is shared across calls.
    /// </summary>
    internal sealed class FunctionVersionCache
    {
        internal static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
        internal const int MaxEntries = 10_000;

        private readonly ConcurrentDictionary<(string TenantId, string VersionId), (FunctionVersionEntity Version, DateTimeOffset ExpiresAt)> _entries = new();
        private readonly TimeProvider _time;

        public FunctionVersionCache(TimeProvider? time = null)
        {
            _time = time ?? TimeProvider.System;
        }

        internal int Count => _entries.Count;

        public async Task<FunctionVersionEntity?> GetOrReadAsync(
            string tenantId, string versionId, Func<Task<FunctionVersionEntity?>> read)
        {
            var key = (tenantId, versionId);
            var now = _time.GetUtcNow();
            if (_entries.TryGetValue(key, out var hit) && hit.ExpiresAt > now)
            {
                return hit.Version;
            }

            var version = await read().ConfigureAwait(false);
            if (version is null)
            {
                _entries.TryRemove(key, out _);
                return null;
            }

            if (_entries.Count >= MaxEntries)
            {
                _entries.Clear();
            }

            _entries[key] = (version, now + Lifetime);
            return version;
        }
    }
}
