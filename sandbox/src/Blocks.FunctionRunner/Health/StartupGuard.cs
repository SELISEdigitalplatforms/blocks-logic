using Blocks.FunctionRunner.Options;
using Docker.DotNet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Health
{
    /// <summary>The result of checking whether this host may execute tenant code.</summary>
    public sealed record HostReadiness(bool Healthy, bool GvisorOk, IReadOnlyList<string> Problems)
    {
        public string Summary => Problems.Count == 0 ? "ready" : string.Join("; ", Problems);
    }

    /// <summary>
    /// Decides whether the runner may consume work.
    /// <para>
    /// The important rule here is the one that cannot be softened: <b>if runsc is missing or
    /// unusable, the runner does not run anything.</b> There is no fallback to runc, no degraded
    /// mode and no override, because the whole isolation story rests on gVisor. An unhealthy
    /// runner keeps heartbeating so an operator can see why, but it claims no work.
    /// </para>
    /// </summary>
    public sealed class StartupGuard
    {
        private readonly IDockerClient _docker;
        private readonly IConnectionMultiplexer? _redis;
        private readonly IDatabase _db;
        private readonly RunnerOptions _options;
        private readonly ILogger<StartupGuard> _logger;

        public StartupGuard(
            IDockerClient docker,
            IDatabase db,
            IOptions<RunnerOptions> options,
            ILogger<StartupGuard> logger,
            IConnectionMultiplexer? redis = null)
        {
            _docker = docker;
            _db = db;
            _redis = redis;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<HostReadiness> CheckAsync(CancellationToken token)
        {
            var problems = new List<string>();
            var gvisorOk = false;

            // --- Docker ------------------------------------------------------------------
            try
            {
                await _docker.System.PingAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                problems.Add($"the Docker Engine is unreachable: {ex.Message}");
                return new HostReadiness(false, false, problems);
            }

            // --- gVisor: mandatory, never optional ---------------------------------------
            try
            {
                var info = await _docker.System.GetSystemInfoAsync(token).ConfigureAwait(false);
                if (info.Runtimes is not null && info.Runtimes.ContainsKey(_options.Runtime))
                {
                    gvisorOk = true;
                }
                else
                {
                    problems.Add(
                        $"the '{_options.Runtime}' runtime is not registered with the Docker Engine; " +
                        "the runner will not execute tenant code without gVisor");
                }
            }
            catch (Exception ex)
            {
                problems.Add($"could not read the Docker system info: {ex.Message}");
            }

            // --- the confined network ----------------------------------------------------
            try
            {
                var networks = await _docker.Networks.ListNetworksAsync(cancellationToken: token).ConfigureAwait(false);
                if (!networks.Any(n => string.Equals(n.Name, _options.Network, StringComparison.Ordinal)))
                {
                    problems.Add($"the '{_options.Network}' network does not exist; run provision/30-network.sh");
                }
            }
            catch (Exception ex)
            {
                problems.Add($"could not list Docker networks: {ex.Message}");
            }

            // --- resolv.conf: without it, sandbox DNS silently fails ----------------------
            if (!File.Exists(_options.ResolvConf))
            {
                problems.Add(
                    $"'{_options.ResolvConf}' is missing; Docker's embedded resolver is unreachable " +
                    "from a gVisor sandbox, so DNS would fail for every function");
            }

            // --- writable state directories ----------------------------------------------
            foreach (var dir in new[] { _options.RunsDir, _options.BuildsDir })
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    var probe = Path.Combine(dir, $".probe-{Guid.NewGuid():N}");
                    await File.WriteAllTextAsync(probe, "ok", token).ConfigureAwait(false);
                    File.Delete(probe);
                }
                catch (Exception ex)
                {
                    problems.Add($"'{dir}' is not writable: {ex.Message}");
                }
            }

            // --- Redis --------------------------------------------------------------------
            try
            {
                await _db.PingAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                problems.Add($"Redis is unreachable: {ex.Message}");
            }

            // The delivery contract assumes a result is durable before its entry is acked, which
            // is only true with AOF on. Warn rather than refuse: it is the operator's call.
            await WarnIfAppendOnlyDisabledAsync().ConfigureAwait(false);

            var healthy = problems.Count == 0 && gvisorOk;
            return new HostReadiness(healthy, gvisorOk, problems);
        }

        private async Task WarnIfAppendOnlyDisabledAsync()
        {
            if (_redis is null) return;
            try
            {
                var endpoint = _redis.GetEndPoints().FirstOrDefault();
                if (endpoint is null) return;

                var server = _redis.GetServer(endpoint);
                var config = await server.ConfigGetAsync("appendonly").ConfigureAwait(false);
                var value = config.FirstOrDefault().Value;
                if (!string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Redis has appendonly='{Value}'. The delivery contract acknowledges a run only " +
                        "once its result is durable, which assumes AOF persistence; results may be lost " +
                        "on a Redis restart.", value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Could not read the Redis appendonly setting: {Message}", ex.Message);
            }
        }
    }
}
