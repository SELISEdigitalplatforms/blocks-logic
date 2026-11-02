using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Sandbox;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Health
{
    /// <summary>
    /// Takes one <see cref="MetricsSample"/>: asks Redis, Docker, the warm pool and the kernel.
    /// <para>
    /// Every source is independent. A Redis or Docker error leaves only its own fields null — a
    /// host in trouble is exactly when the other numbers are wanted most.
    /// </para>
    /// </summary>
    public sealed class SampleCollector
    {
        private readonly IDatabase _db;
        private readonly IDockerClient _docker;
        private readonly HeartbeatService _heartbeat;
        private readonly WarmPool _warm;
        private readonly HostProbe _probe;
        private readonly RunnerOptions _options;
        private readonly TimeProvider _clock;

        public SampleCollector(
            IDatabase db,
            IDockerClient docker,
            HeartbeatService heartbeat,
            WarmPool warm,
            IOptions<RunnerOptions> options,
            TimeProvider clock)
        {
            _db = db;
            _docker = docker;
            _heartbeat = heartbeat;
            _warm = warm;
            _options = options.Value;
            _clock = clock;
            _probe = new HostProbe();
        }

        public async Task<MetricsSample> CollectAsync(CancellationToken token)
        {
            var (total, busy, idle) = _warm.Counts;
            return new MetricsSample(
                _clock.GetUtcNow(),
                _options.RunnerId,
                _heartbeat.Latest?.Healthy ?? false,
                new WarmSample(total, busy, idle),
                await ReadStreamsAsync().ConfigureAwait(false),
                _probe.Read(),
                await ReadDockerAsync(token).ConfigureAwait(false));
        }

        private async Task<IReadOnlyDictionary<string, StreamSample>> ReadStreamsAsync()
        {
            var keys = new Dictionary<string, string>
            {
                [StreamNames.Runs] = RedisKeys.RunsStream,
                [StreamNames.EventRuns] = RedisKeys.EventRunsStream,
                [StreamNames.Tests] = RedisKeys.TestsStream,
                [StreamNames.Builds] = RedisKeys.BuildsStream,
            };

            var result = new Dictionary<string, StreamSample>();
            foreach (var (name, key) in keys)
            {
                result[name] = await ReadStreamAsync(key).ConfigureAwait(false);
            }
            return result;
        }

        private async Task<StreamSample> ReadStreamAsync(string key)
        {
            long? length = null;
            long? pending = null;
            long? lag = null;
            try
            {
                length = await _db.StreamLengthAsync(key).ConfigureAwait(false);
                var group = (await _db.StreamGroupInfoAsync(key).ConfigureAwait(false))
                    .FirstOrDefault(g => g.Name == RedisKeys.RunnerGroup);
                if (group.Name is not null)
                {
                    pending = group.PendingMessageCount;
                    lag = group.Lag;
                }
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                // Redis down or the group not created yet: the fields stay null.
            }
            return new StreamSample(length, pending, lag);
        }

        private async Task<DockerStats> ReadDockerAsync(CancellationToken token)
        {
            // Shims first, then Docker: a container created in between then shows up in Docker's
            // list (not as an orphan). The reverse order would flag every new container.
            var shims = ShimScanner.Scan();
            try
            {
                var containers = await _docker.Containers
                    .ListContainersAsync(new ContainersListParameters { All = true }, token).ConfigureAwait(false);
                var ids = containers.Select(c => c.ID).ToHashSet(StringComparer.Ordinal);
                var running = containers.Count(c => c.State == "running"
                    && c.Labels is not null && c.Labels.ContainsKey(SandboxProfile.SandboxLabel));
                var orphans = ShimScanner.Orphans(shims, ids, _clock.GetUtcNow(), HealthThresholds.SampleOrphanGrace);
                return new DockerStats(running, containers.Count, shims.Count, orphans.Count);
            }
            catch (Exception ex) when (ex is DockerApiException or HttpRequestException or TimeoutException or IOException)
            {
                return new DockerStats(null, null, shims.Count, null);
            }
        }
    }
}
