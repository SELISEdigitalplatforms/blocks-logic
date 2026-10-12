using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Sandbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.FunctionRunner.Maintenance
{
    /// <summary>When this host last ran each function image.</summary>
    public interface IImageUsageLog
    {
        /// <summary>Records that <paramref name="reference"/> was just used. Never throws.</summary>
        void Touch(string reference);

        /// <summary>
        /// When <paramref name="reference"/> was last used, or null if this host has no record of it
        /// — which is the case for an image built before this log existed, and for one restored from
        /// a disk that was wiped.
        /// </summary>
        DateTime? LastUsedUtc(string reference);
    }

    /// <summary>
    /// A file per image, whose modified time is the last time this host ran it.
    /// <para>
    /// Docker does not record when an image was last used — only when it was created — and created
    /// is the wrong order to evict in: a function built a year ago and called every minute would go
    /// before one built yesterday and never called since. Eviction is supposed to keep what is busy.
    /// </para>
    /// <para>
    /// Deliberately on local disk rather than in Redis. It describes what <i>this</i> host has, it is
    /// worthless to any other host, and it is pure cache — losing it costs one rebuild per image, not
    /// correctness. An empty file touched per run is also cheap enough not to think about.
    /// </para>
    /// </summary>
    public sealed class ImageUsageLog : IImageUsageLog
    {
        private readonly string _directory;
        private readonly ILogger<ImageUsageLog> _logger;

        public ImageUsageLog(IOptions<RunnerOptions> options, ILogger<ImageUsageLog> logger)
        {
            _logger = logger;
            _directory = Path.Combine(options.Value.ArtifactsDir, "usage");
        }

        private string PathFor(string reference) =>
            Path.Combine(_directory, ArtifactImageBuilder.Sanitize(reference));

        /// <inheritdoc />
        public void Touch(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return;

            try
            {
                Directory.CreateDirectory(_directory);
                var path = PathFor(reference);

                // Create-or-stamp. The file's content is never read; only its timestamp matters.
                if (File.Exists(path))
                {
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                }
                else
                {
                    File.WriteAllBytes(path, []);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A run must never fail because bookkeeping did. The cost of losing a stamp is that
                // this image looks colder than it is, and might be evicted and rebuilt once.
                _logger.LogDebug("Could not record use of {Reference}: {Message}", reference, ex.Message);
            }
        }

        /// <inheritdoc />
        public DateTime? LastUsedUtc(string reference)
        {
            try
            {
                var path = PathFor(reference);
                return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
