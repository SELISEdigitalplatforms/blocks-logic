using System.Text.Json;
using Blocks.FunctionRunner.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.FunctionRunner.Sandbox
{
    /// <summary>
    /// The versions this host keeps warm sandboxes for, kept on disk so a restarted runner can warm
    /// them again instead of making each one's next caller pay a cold start (~10 s measured
    /// 2026-10-06). Keys only — tenant, function, version, image; no limits (one fixed profile), no
    /// secrets, no input. Written whenever the set of versions changes, never on shutdown (the
    /// shutdown that empties the pool is exactly what this outlives).
    /// <para>
    /// Best effort throughout: an unreadable or unwritable file means a cold first call, as before.
    /// </para>
    /// </summary>
    public sealed class WarmKeyJournal
    {
        /// <summary>At most this many versions are remembered — a restart is not a reason to start dozens.</summary>
        internal const int MaxKeys = 20;

        private readonly string _path;
        private readonly ILogger<WarmKeyJournal> _logger;
        private readonly Lock _write = new();

        public WarmKeyJournal(IOptions<RunnerOptions> options, ILogger<WarmKeyJournal> logger)
            : this(Path.Combine(Path.GetDirectoryName(options.Value.RunsDir.TrimEnd('/')) ?? options.Value.RunsDir, "warm-keys.json"), logger)
        {
        }

        internal WarmKeyJournal(string path, ILogger<WarmKeyJournal> logger)
        {
            _path = path;
            _logger = logger;
        }

        public IReadOnlyList<WarmKey> Load()
        {
            try
            {
                if (!File.Exists(_path)) return [];
                var keys = JsonSerializer.Deserialize<List<WarmKey>>(File.ReadAllText(_path)) ?? [];
                return [.. keys
                    .Where(k => !string.IsNullOrWhiteSpace(k.FunctionId) && !string.IsNullOrWhiteSpace(k.Image))
                    .Distinct()
                    .Take(MaxKeys)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogWarning("Could not read the warm-version list {Path}: {Message}", _path, ex.Message);
                return [];
            }
        }

        public void Save(IEnumerable<WarmKey> keys)
        {
            var list = keys.Distinct().Take(MaxKeys).ToList();
            lock (_write)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    // Written aside and renamed over, so a crash mid-write never leaves half a file.
                    var temp = _path + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(list));
                    File.Move(temp, _path, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug("Could not write the warm-version list {Path}: {Message}", _path, ex.Message);
                }
            }
        }
    }
}
