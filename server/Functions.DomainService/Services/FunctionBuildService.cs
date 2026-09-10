using System.Text.Json;
using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Services
{
    public interface IFunctionBuildService
    {
        /// <summary>
        /// Ensures a built, digest-pinned image exists for <paramref name="function"/>'s
        /// current source, reusing a cached build when one exists (DECISIONS D3). Waits up to
        /// <c>Functions:BuildWaitSeconds</c> (default 300 s) for an in-flight build to finish.
        /// <para>
        /// <paramref name="waitSecondsOverride"/> shortens that wait. Test passes a few seconds:
        /// holding an editor's request open for five minutes and then answering "still building"
        /// tells the caller nothing it could not have polled for, so it takes the build back
        /// unfinished and reports progress instead.
        /// </para>
        /// </summary>
        Task<FunctionBuildEntity> EnsureImageAsync(
            string tenantId,
            FunctionEntity function,
            CancellationToken cancellationToken = default,
            int? waitSecondsOverride = null);

        /// <summary>A single build's current state — for the editor's build-progress indicator to poll.</summary>
        Task<FunctionBuildEntity> GetAsync(string tenantId, string buildId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One build serves both Test and Deploy (DECISIONS D3): both call
    /// <see cref="EnsureImageAsync"/> with the same source hash, so a Test click and a
    /// following Deploy of the same unmodified source never rebuild.
    /// <para>
    /// The build itself happens on the Runner VM; this service only queues the job on
    /// <c>functions:builds</c> and polls the Mongo record that
    /// <c>FunctionBuildResultConsumer</c> updates when the runner reports back on
    /// <c>functions:build-results</c>. It never talks to Docker or the registry directly —
    /// that machinery, and the untrusted <c>npm install</c> it runs, lives entirely on the
    /// other side of the Redis contract (plan/PROTOCOL.md), on purpose.
    /// </para>
    /// </summary>
    public class FunctionBuildService : IFunctionBuildService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private readonly IFunctionBuildRepository _buildRepository;
        private readonly ICacheClient _cache;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FunctionBuildService> _logger;

        public FunctionBuildService(
            IFunctionBuildRepository buildRepository,
            ICacheClient cache,
            IConfiguration configuration,
            ILogger<FunctionBuildService> logger)
        {
            _buildRepository = buildRepository;
            _cache = cache;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<FunctionBuildEntity> GetAsync(
            string tenantId, string buildId, CancellationToken cancellationToken = default)
        {
            var build = await _buildRepository.GetByIdAsync(tenantId, buildId, cancellationToken);
            return build ?? throw new Utils.FunctionNotFoundException($"build '{buildId}' was not found");
        }

        public async Task<FunctionBuildEntity> EnsureImageAsync(
            string tenantId,
            FunctionEntity function,
            CancellationToken cancellationToken = default,
            int? waitSecondsOverride = null)
        {
            var sourceHash = function.SourceHash;

            var succeeded = await _buildRepository.GetSucceededBySourceHashAsync(
                tenantId, function.ItemId, sourceHash, cancellationToken);
            if (succeeded is not null) return succeeded;

            var inProgress = await _buildRepository.GetInProgressBySourceHashAsync(
                tenantId, function.ItemId, sourceHash, cancellationToken);

            // A queued build that no runner ever claimed would otherwise block this source hash for
            // good: every later Test finds the same in-flight record and waits on a job that is not
            // coming. It happens for real — an entry added to `functions:builds` before a runner's
            // consumer group existed on that Redis is never delivered to anyone, and the source
            // payload behind it expires after SourceTtl. So a record that has not moved within the
            // stale window is retired and a fresh build is queued in its place.
            if (inProgress is not null && IsStale(inProgress))
            {
                _logger.LogWarning(
                    "Build {BuildId} for function {FunctionId} has been {Status} since {Updated:o} " +
                    "and was never claimed; retiring it and queueing a replacement",
                    inProgress.ItemId, function.ItemId, inProgress.Status, inProgress.LastUpdatedDate);

                await _buildRepository.ApplyResultAsync(
                    tenantId,
                    inProgress.ItemId,
                    BuildStatus.Failed,
                    imageDigest: null,
                    packages: null,
                    log: null,
                    errorMessage: "the build was never picked up by a runner and has been retired",
                    completedAt: DateTime.UtcNow,
                    cancellationToken).ConfigureAwait(false);

                inProgress = null;
            }

            var build = inProgress ?? await QueueBuildAsync(tenantId, function, sourceHash, cancellationToken);

            return await WaitForCompletionAsync(tenantId, build, cancellationToken, waitSecondsOverride);
        }

        private async Task<FunctionBuildEntity> QueueBuildAsync(
            string tenantId, FunctionEntity function, string sourceHash, CancellationToken cancellationToken)
        {
            // Both hashes are full sha256 hex (64 chars), so the tag was 64 + 1 + 64 = 129 —
            // one character past Docker's 128-char tag limit. Every build died on
            // `invalid reference format` before it started. Twelve hex characters each (48 bits,
            // 96 combined) is the usual short-digest convention and leaves the tag at 25.
            const int TagHashChars = 12;
            var codeHash = Utils.FunctionHashing.CodeHash(function.Source);
            var manifestHash = Utils.FunctionHashing.ManifestHash(function.Source);
            var registry = _configuration["Functions:Registry"] ?? "127.0.0.1:5000";
            var imageTag =
                $"{codeHash[..Math.Min(TagHashChars, codeHash.Length)]}-" +
                $"{manifestHash[..Math.Min(TagHashChars, manifestHash.Length)]}";
            var imageRef = $"{registry}/fn/{function.ItemId}:{imageTag}";

            var build = new FunctionBuildEntity
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                FunctionId = function.ItemId,
                SourceHash = sourceHash,
                Status = BuildStatus.Queued,
            };
            await _buildRepository.CreateAsync(tenantId, build, cancellationToken);

            var sourceBundle = JsonSerializer.Serialize(new
            {
                files = BuildFileMap(function.Source),
            });

            var database = _cache.CacheDatabase();
            var sourceKey = FunctionQueueKeys.Source(build.ItemId);
            await database.StringSetAsync(sourceKey, sourceBundle, FunctionQueueKeys.SourceTtl);

            await database.StreamAddAsync(FunctionQueueKeys.BuildsStream,
            [
                new NameValueEntry("buildId", build.ItemId),
                new NameValueEntry("functionId", function.ItemId),
                new NameValueEntry("tenantId", tenantId),
                new NameValueEntry("sourceKey", sourceKey),
                new NameValueEntry("imageRef", imageRef),
                new NameValueEntry("allowScripts", "false"),
                new NameValueEntry("protocol", FunctionQueueKeys.ProtocolVersion),
            ]);

            _logger.LogInformation(
                "Queued build {BuildId} for function {FunctionId} (source hash {SourceHash})",
                build.ItemId, function.ItemId, sourceHash);

            return build;
        }

        private static Dictionary<string, string> BuildFileMap(Models.FunctionSource source)
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["index.js"] = source.IndexJs,
                ["package.json"] = source.PackageJson,
            };
            if (!string.IsNullOrEmpty(source.LockJson))
            {
                files["package-lock.json"] = source.LockJson;
            }
            return files;
        }

        /// <summary>
        /// A queued or building record that has not been touched within
        /// <c>Functions:BuildStaleAfterSeconds</c> (default 600 s). A runner claims within seconds
        /// and moves the record to Building, so silence well past that means nobody is working on it.
        /// </summary>
        private bool IsStale(FunctionBuildEntity build)
        {
            var staleAfter = _configuration.GetValue("Functions:BuildStaleAfterSeconds", 600);
            return build.LastUpdatedDate < DateTime.UtcNow.AddSeconds(-staleAfter);
        }

        private async Task<FunctionBuildEntity> WaitForCompletionAsync(
            string tenantId,
            FunctionBuildEntity build,
            CancellationToken cancellationToken,
            int? waitSecondsOverride = null)
        {
            var waitSeconds = waitSecondsOverride
                ?? _configuration.GetValue("Functions:BuildWaitSeconds", 300);
            var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
            var current = build;

            while (current.Status is BuildStatus.Queued or BuildStatus.Building && DateTime.UtcNow < deadline)
            {
                await Task.Delay(PollInterval, cancellationToken);
                var refreshed = await _buildRepository.GetByIdAsync(tenantId, build.ItemId, cancellationToken);
                if (refreshed is not null) current = refreshed;
            }

            if (current.Status is BuildStatus.Queued or BuildStatus.Building)
            {
                _logger.LogWarning(
                    "Build {BuildId} for function {FunctionId} did not finish within {Seconds}s; returning in-progress",
                    build.ItemId, build.FunctionId, waitSeconds);
            }

            return current;
        }
    }
}
