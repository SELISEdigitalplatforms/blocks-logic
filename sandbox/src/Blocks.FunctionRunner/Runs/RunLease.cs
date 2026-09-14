using Blocks.FunctionRunner.Contracts;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Runs
{
    /// <summary>
    /// Holds the execution lease for one run and watches for a cancel request.
    /// <para>
    /// The lease is what stops two runners executing the same run at once. It is taken with
    /// SET NX PX, renewed at a third of its TTL, and released on the way out. If this runner
    /// dies, the lease expires on its own and XAUTOCLAIM hands the entry to somebody else —
    /// which is exactly the at-least-once behaviour the contract promises, not a bug.
    /// </para>
    /// <para>
    /// Renewal is conditional: it only extends a lease this runner still owns. A renewal that
    /// finds someone else's token gives up and cancels the run, because continuing would mean
    /// two sandboxes for one run with neither aware of the other.
    /// </para>
    /// </summary>
    public sealed class RunLease : IAsyncDisposable
    {
        // Renew only if the value is still ours; otherwise report the loss.
        private const string RenewScript = """
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('pexpire', KEYS[1], ARGV[2])
            else
                return 0
            end
            """;

        private const string ReleaseScript = """
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('del', KEYS[1])
            else
                return 0
            end
            """;

        private readonly IDatabase _db;
        private readonly ILogger _logger;
        private readonly string _runId;
        private readonly string _leaseKey;
        private readonly string _token;
        private readonly TimeSpan _ttl;
        private readonly CancellationTokenSource _cts;
        private readonly Task _renewalLoop;

        private RunLease(IDatabase db, ILogger logger, string runId, string token, TimeSpan ttl, CancellationToken outer)
        {
            _db = db;
            _logger = logger;
            _runId = runId;
            _leaseKey = RedisKeys.Lease(runId);
            _token = token;
            _ttl = ttl;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
            _renewalLoop = Task.Run(() => RenewLoopAsync(_cts.Token), CancellationToken.None);
        }

        /// <summary>Cancelled when the lease is lost or the control plane asks for a cancel.</summary>
        public CancellationToken Token => _cts.Token;

        /// <summary>True when the run was cancelled on request rather than by losing the lease.</summary>
        public bool CancelRequested { get; private set; }

        /// <summary>True when another runner took the lease from under us.</summary>
        public bool LeaseLost { get; private set; }

        /// <summary>
        /// Takes the lease, or returns null when another runner already holds it.
        /// </summary>
        public static async Task<RunLease?> TryAcquireAsync(
            IDatabase db, ILogger logger, string runId, TimeSpan ttl, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(db);

            var candidate = Guid.NewGuid().ToString("N");
            var acquired = await db.StringSetAsync(
                RedisKeys.Lease(runId), candidate, ttl, when: When.NotExists).ConfigureAwait(false);

            return acquired ? new RunLease(db, logger, runId, candidate, ttl, token) : null;
        }

        private async Task RenewLoopAsync(CancellationToken token)
        {
            var interval = TimeSpan.FromMilliseconds(Math.Max(500, _ttl.TotalMilliseconds / 3));
            var cancelKey = RedisKeys.Cancel(_runId);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(interval, token).ConfigureAwait(false);

                    // A cancel request is checked on the same beat as the renewal, so a cancel
                    // is acted on within a third of a lease at worst.
                    if (await _db.KeyExistsAsync(cancelKey).ConfigureAwait(false))
                    {
                        CancelRequested = true;
                        _logger.LogInformation("Cancel requested for run {RunId}", _runId);
                        await _cts.CancelAsync().ConfigureAwait(false);
                        return;
                    }

                    var renewed = (int)(long)await _db.ScriptEvaluateAsync(
                        RenewScript, [_leaseKey], [_token, (long)_ttl.TotalMilliseconds]).ConfigureAwait(false);

                    if (renewed == 0)
                    {
                        LeaseLost = true;
                        _logger.LogError(
                            "Lost the lease for run {RunId} — another runner has taken it; abandoning this execution",
                            _runId);
                        await _cts.CancelAsync().ConfigureAwait(false);
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (RedisException ex)
            {
                // Redis being briefly unavailable is not proof the lease was lost; keep the run
                // going and let the hard deadline bound it.
                _logger.LogWarning("Lease renewal for run {RunId} failed: {Message}", _runId, ex.Message);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await _renewalLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }

            if (!LeaseLost)
            {
                try
                {
                    await _db.ScriptEvaluateAsync(ReleaseScript, [_leaseKey], [_token]).ConfigureAwait(false);
                }
                catch (RedisException ex)
                {
                    _logger.LogWarning("Could not release the lease for run {RunId}: {Message}", _runId, ex.Message);
                }
            }

            _cts.Dispose();
        }
    }
}
