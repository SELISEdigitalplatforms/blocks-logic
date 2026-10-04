using System.Security.Cryptography;
using System.Text;
using Blocks.FunctionRunner.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.FunctionRunner.Builds
{
    /// <summary>Installed dependency trees, kept so an unchanged manifest does not install again.</summary>
    public interface IDependencyCache
    {
        /// <summary>
        /// Puts a previously installed tree at <paramref name="destination"/> if one was kept for
        /// this exact manifest, and reports whether it did.
        /// </summary>
        bool TryRestore(string? tenantId, string manifest, string destination);

        /// <summary>Keeps the tree just installed, for the next build of the same manifest.</summary>
        void Save(string? tenantId, string manifest, string archivePath);
    }

    /// <summary>
    /// A tarball per <c>package.json</c>, on this host.
    /// <para>
    /// The observation it rests on: while someone is writing a function they change
    /// <c>index.js</c> constantly and <c>package.json</c> almost never — and changing the
    /// <em>input</em> they are testing with changes neither. Every one of those builds reinstalled
    /// the same dependency tree from scratch, which is minutes of CPU and the thing that made a
    /// developer clicking Test cost the deployed version its capacity.
    /// </para>
    /// <para>
    /// Keyed by the manifest, so a dependency change still installs — that is the one moment the
    /// tree can legitimately differ. Keyed by tenant as well, because a build output is a tenant's
    /// own and two tenants with identical manifests must not share one.
    /// </para>
    /// <para>
    /// Reusing rather than re-resolving also makes the tree <em>stabler</em>: today two builds of
    /// the same unchanged <c>package.json</c> can resolve different versions on different days,
    /// because a caret range means whatever the registry offers at that moment.
    /// </para>
    /// </summary>
    public sealed class DependencyCache : IDependencyCache
    {
        private readonly RunnerOptions _options;
        private readonly ILogger<DependencyCache> _logger;

        public DependencyCache(IOptions<RunnerOptions> options, ILogger<DependencyCache> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        private string Root => Path.Combine(_options.ArtifactsDir, "deps-cache");

        /// <summary>
        /// <c>{tenant}-{manifest hash}.tar</c>, both hashed so neither can shape a path. The tenant
        /// is part of the key, not just the directory, so a missing tenant id cannot collide with
        /// another tenant's entry.
        /// </summary>
        internal static string KeyFor(string? tenantId, string manifest)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(manifest);

            var material = (tenantId ?? "no-tenant") + "\n" + manifest;
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        }

        private string PathFor(string? tenantId, string manifest) =>
            Path.Combine(Root, KeyFor(tenantId, manifest) + ".tar");

        /// <inheritdoc />
        public bool TryRestore(string? tenantId, string manifest, string destination)
        {
            if (!_options.CacheDependencies) return false;

            try
            {
                var cached = PathFor(tenantId, manifest);
                if (!File.Exists(cached)) return false;

                File.Copy(cached, destination, overwrite: true);

                // Its freshness is what the eviction sweep reads, so a tree in daily use outlives
                // one installed once and forgotten.
                File.SetLastWriteTimeUtc(cached, DateTime.UtcNow);

                _logger.LogInformation(
                    "Reusing the installed dependencies for this manifest; skipping npm install");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A cache that cannot be read is not a failure — it is a cache. Install instead.
                _logger.LogDebug("Could not restore cached dependencies: {Message}", ex.Message);
                return false;
            }
        }

        /// <inheritdoc />
        public void Save(string? tenantId, string manifest, string archivePath)
        {
            if (!_options.CacheDependencies) return;

            try
            {
                Directory.CreateDirectory(Root);
                var cached = PathFor(tenantId, manifest);

                // Written beside the target and moved into place, so a build interrupted halfway
                // never leaves a half-written tree for the next build to restore and trust.
                var staging = cached + ".partial";
                File.Copy(archivePath, staging, overwrite: true);
                File.Move(staging, cached, overwrite: true);

                Evict();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug("Could not cache the installed dependencies: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// Keeps the cache inside its entry budget, oldest-used first.
        /// <para>
        /// Deliberately its own small budget rather than a share of the disk: the image cache is on
        /// the same disk, and a dependency cache that grew into it would evict the images deployed
        /// functions are running from. Helping tests must not cost production.
        /// </para>
        /// </summary>
        private void Evict()
        {
            var files = new DirectoryInfo(Root).GetFiles("*.tar");
            var excess = files.Length - _options.MaxCachedDependencyTrees;
            if (excess <= 0) return;

            foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc).Take(excess))
            {
                try
                {
                    file.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug("Could not evict {File}: {Message}", file.Name, ex.Message);
                }
            }
        }
    }
}
