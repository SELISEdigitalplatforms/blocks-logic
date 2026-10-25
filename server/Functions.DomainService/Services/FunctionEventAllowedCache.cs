using System.Collections.Concurrent;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// The event types a deployed version may send, kept in memory by version id for the invocation hot path — every
    /// run asks, and most functions send no events. Safe for the same reason as <see cref="FunctionVersionCache"/>: a
    /// version's events row is a copy made at deploy, before the version can be called, and never changes after. An
    /// empty answer is kept too (a version without a row). Entries expire after <see cref="Lifetime"/>; past
    /// <see cref="MaxEntries"/> the map is dropped.
    /// </summary>
    internal sealed class FunctionEventAllowedCache
    {
        internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
        internal const int MaxEntries = 10_000;

        private readonly ConcurrentDictionary<(string TenantId, string VersionId), (IReadOnlyCollection<string> Types, DateTimeOffset ExpiresAt)> _entries = new();
        private readonly TimeProvider _time;

        public FunctionEventAllowedCache(TimeProvider? time = null)
        {
            _time = time ?? TimeProvider.System;
        }

        public async Task<IReadOnlyCollection<string>> GetOrReadAsync(
            string tenantId, string versionId, Func<Task<IReadOnlyCollection<string>>> read)
        {
            var key = (tenantId, versionId);
            var now = _time.GetUtcNow();
            if (_entries.TryGetValue(key, out var hit) && hit.ExpiresAt > now)
            {
                return hit.Types;
            }

            var types = await read().ConfigureAwait(false);
            if (_entries.Count >= MaxEntries)
            {
                _entries.Clear();
            }

            _entries[key] = (types, now + Lifetime);
            return types;
        }
    }
}
