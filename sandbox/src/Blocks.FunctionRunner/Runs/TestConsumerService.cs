using System.Globalization;
using Blocks.FunctionRunner.Builds;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Health;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Redis;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Runs
{
    /// <summary>
    /// Test runs, start to finish, on one host: build the editor's current source into a local
    /// image, run it once, delete the image.
    /// <para>
    /// A test used to be two jobs — a build, whose image was pushed to the builder's registry and
    /// reused for the same source, and a run that any runner could claim. With more than one
    /// runner on a queue those land on different hosts, and a host-local registry means the run
    /// asks for an image its host never had: <c>IMAGE_PULL_FAILED</c>, at random. Here the image
    /// never leaves the host that built it and never outlives the run, so there is nothing for
    /// another host to miss and nothing stale to reuse.
    /// </para>
    /// <para>
    /// Every way out deletes the image and reports the run: the build failing, the run's payload
    /// having gone (the function was deleted), a cancel arriving during the build, the host being
    /// out of capacity, the run itself. A runner that dies half way leaves the entry pending; it
    /// is reclaimed and the next runner builds from the source again, and Image GC removes the
    /// image the dead one left.
    /// </para>
    /// </summary>
    public sealed class TestConsumerService : BackgroundService
    {
        /// <summary>How long a built test waits for a sandbox slot before giving up.</summary>
        internal static readonly TimeSpan AdmissionWait = TimeSpan.FromMinutes(2);

        private readonly IDatabase _db;
        private readonly BuildProcessor _builds;
        private readonly RunProcessor _runs;
        private readonly IDockerClient _docker;
        private readonly HeartbeatService _heartbeat;
        private readonly RunnerOptions _options;
        private readonly ILogger<TestConsumerService> _logger;

        public TestConsumerService(
            IDatabase db,
            BuildProcessor builds,
            RunProcessor runs,
            IDockerClient docker,
            HeartbeatService heartbeat,
            IOptions<RunnerOptions> options,
            ILogger<TestConsumerService> logger)
        {
            _db = db;
            _builds = builds;
            _runs = runs;
            _docker = docker;
            _heartbeat = heartbeat;
            _options = options.Value;
            _logger = logger;
        }

        /// <summary>
        /// A test entry is only abandoned once it has been idle longer than a whole test can take
        /// — the build budget, the admission wait and the longest run — so a slow build is never
        /// taken from a runner that is still working on it.
        /// </summary>
        internal TimeSpan ReclaimAfter =>
            TimeSpan.FromSeconds(_options.BuildTimeoutSeconds) + AdmissionWait + TimeSpan.FromSeconds(Ceilings.TimeoutSeconds) + TimeSpan.FromMinutes(2);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.ProcessBuilds)
            {
                _logger.LogInformation("Build processing is disabled on this host, so it takes no test runs");
                return;
            }

            var consumer = new GroupConsumer(
                _db, _logger, RedisKeys.TestsStream, RedisKeys.RunnerGroup, _options.RunnerId);
            await consumer.EnsureGroupAsync().ConfigureAwait(false);

            _logger.LogInformation("Consuming {Stream} as {Consumer}", RedisKeys.TestsStream, _options.RunnerId);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_heartbeat.Latest is { Healthy: false })
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    var entries = await consumer.ReadNewAsync(count: 1, stoppingToken).ConfigureAwait(false);
                    if (entries.Count == 0)
                    {
                        entries = await consumer.ReclaimAbandonedAsync(ReclaimAfter, count: 1).ConfigureAwait(false);
                    }
                    if (entries.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    foreach (var entry in entries)
                    {
                        await HandleAsync(consumer, entry, stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "The test loop hit an unexpected error");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                }
            }

            _logger.LogInformation("Test loop stopped");
        }

        private async Task HandleAsync(GroupConsumer consumer, ClaimedEntry entry, CancellationToken token)
        {
            var runId = entry.Get("runId");
            var buildId = entry.Get("buildId");
            if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(buildId))
            {
                await consumer.DeadLetterAsync(entry, "the test entry carries no runId or buildId").ConfigureAwait(false);
                return;
            }

            if (RunConsumerService.ReadProtocol(entry) is not { } protocol)
            {
                await consumer.DeadLetterAsync(entry,
                    $"protocol version '{entry.Get("protocol")}' is not supported by this runner").ConfigureAwait(false);
                return;
            }

            var deliveries = await consumer.DeliveryCountAsync(entry).ConfigureAwait(false);
            if (deliveries > _options.MaxAttempts)
            {
                await consumer.DeadLetterAsync(entry, $"delivered {deliveries} times without completing").ConfigureAwait(false);
                return;
            }

            // Local to this host and named after the build, so a reclaimed entry rebuilds under
            // the same name on its new host and nothing here can collide with a deployed image.
            var imageRef = TestImageRef(buildId);
            var run = new RunJob
            {
                RunId = runId,
                FunctionId = entry.Get("functionId") ?? runId,
                TenantId = entry.Get("tenantId"),
                Image = imageRef,
                Attempt = RunConsumerService.ReadAttempt(entry),
                Deliveries = deliveries,
                Protocol = protocol,
            };

            try
            {
                await RunTestAsync(run, entry, buildId, imageRef, token).ConfigureAwait(false);
            }
            finally
            {
                await DeleteImageAsync(imageRef).ConfigureAwait(false);
            }

            // Reached on every path that reported the run (or found nothing left to report to).
            await consumer.AcknowledgeAsync(entry.Id).ConfigureAwait(false);
        }

        private async Task RunTestAsync(RunJob run, ClaimedEntry entry, string buildId, string imageRef, CancellationToken token)
        {
            // Nothing to do for a run whose payload is gone: its function was deleted, or it
            // waited past its TTL. Building it anyway would only make an image nobody runs.
            if (!await _runs.IsLiveAsync(run.RunId).ConfigureAwait(false))
            {
                _logger.LogInformation("Test run {RunId} no longer exists; skipping its build", run.RunId);
                return;
            }

            if (await _runs.IsCancelRequestedAsync(run.RunId).ConfigureAwait(false))
            {
                await _runs.ReportUnexecutedAsync(run, RunStatuses.Cancelled, string.Empty,
                    "the test was cancelled before it started", token).ConfigureAwait(false);
                return;
            }

            // --- build, for this host only ---------------------------------------------------
            // Shown as "building" in the console: without it the run read Queued for the whole build.
            await _runs.MarkBuildingAsync(run.RunId).ConfigureAwait(false);
            var build = await _builds.ProcessAsync(new BuildJob
            {
                BuildId = buildId,
                FunctionId = run.FunctionId,
                TenantId = run.TenantId,
                SourceKey = entry.Get("sourceKey") ?? RedisKeys.Source(buildId),
                ImageRef = imageRef,
                AllowScripts = string.Equals(entry.Get("allowScripts"), "true", StringComparison.OrdinalIgnoreCase),
                LocalOnly = true,
            }, token).ConfigureAwait(false);

            if (!build.Succeeded || string.IsNullOrEmpty(build.Image))
            {
                await _runs.ReportUnexecutedAsync(run, RunStatuses.Failed, ErrorCodes.BuildFailed,
                    $"the test build failed: {build.Error ?? "unknown error"}", token).ConfigureAwait(false);
                return;
            }

            // A cancel, or a delete, that came in while it was building.
            if (!await _runs.IsLiveAsync(run.RunId).ConfigureAwait(false)) return;
            if (await _runs.IsCancelRequestedAsync(run.RunId).ConfigureAwait(false))
            {
                await _runs.ReportUnexecutedAsync(run, RunStatuses.Cancelled, string.Empty,
                    "the test was cancelled while its image was building", token).ConfigureAwait(false);
                return;
            }

            // --- run, here, on the image just built -----------------------------------------
            // By image id, not tag: nothing between here and the sandbox can re-point it.
            // IsTest here, not on the wire: it is a property of how this job was claimed, and the
            // control plane has no business asserting which budget a runner draws on.
            var job = run with { Image = build.Image, IsTest = true };
            var deadline = DateTimeOffset.UtcNow + AdmissionWait;
            while (true)
            {
                var disposition = await _runs.ProcessAsync(job, token).ConfigureAwait(false);
                if (disposition != RunProcessor.Disposition.Deferred) return;

                if (DateTimeOffset.UtcNow >= deadline)
                {
                    await _runs.ReportUnexecutedAsync(run, RunStatuses.Failed, ErrorCodes.SandboxStartFailed,
                        $"the test was built but no sandbox slot freed up within {AdmissionWait.TotalMinutes:0} minutes; " +
                        "nothing ran — test again", token).ConfigureAwait(false);
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(500), token).ConfigureAwait(false);
            }
        }

        /// <summary>The local name of a test build's image. Never a registry reference.</summary>
        internal static string TestImageRef(string buildId) =>
            string.Create(CultureInfo.InvariantCulture, $"blocks-test/{buildId.ToLowerInvariant()}:local");

        /// <summary>
        /// Removes the test image. Forced, because the sandbox that used it may still be on its way
        /// out; a failure is logged and left to Image GC, which removes stale test images anyway.
        /// </summary>
        private async Task DeleteImageAsync(string imageRef)
        {
            try
            {
                await _docker.Images.DeleteImageAsync(
                    imageRef, new ImageDeleteParameters { Force = true, NoPrune = false }).ConfigureAwait(false);
                _logger.LogInformation("Deleted test image {Image}", imageRef);
            }
            catch (DockerImageNotFoundException)
            {
                // Never built (the build failed) or already gone.
            }
            catch (DockerApiException ex)
            {
                _logger.LogWarning("Could not delete test image {Image}: {Message}; Image GC will", imageRef, ex.Message);
            }
        }
    }
}
