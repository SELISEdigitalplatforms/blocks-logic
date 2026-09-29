using Blocks.Genesis;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Consumers
{
    /// <summary>
    /// The background half of deleting a function. The Api only tombstones it
    /// (<see cref="IFunctionRepository.MarkDeletedAsync"/>) and queues it here; this purges
    /// everything it owned (<see cref="IFunctionPurgeService"/>) and then removes the document.
    /// <para>
    /// <b>Why more than one pass.</b> The tombstone stops anything <i>new</i> from starting, but
    /// work that read the function a moment before it was written keeps going: a deploy waiting
    /// up to <c>Functions:BuildWaitSeconds</c> for its build, then creating a version and pinning
    /// its image; an invocation creating its run record; a result landing and bumping the stats.
    /// A single purge at delete time misses all of that and leaves it for ever — a version with a
    /// pinned image nothing will ever release, a queued run that executes deleted code. So the
    /// purge repeats, at most every <see cref="PassInterval"/>, until the tombstone is older than
    /// the settle window — longer than anything in flight can still take — and only a pass that
    /// <i>starts</i> after that window finishes the delete.
    /// </para>
    /// <para>
    /// <b>Where the work comes from.</b> The Redis set <see cref="FunctionWorkerQueueKeys.PendingDeletions"/>
    /// is the fast path, polled every few seconds. Mongo is the record: a backstop sweep visits
    /// every tenant for tombstones and re-queues any the set lost (Redis flushed, the Api's
    /// enqueue failed, a Worker died mid-pass). Nothing here is ever the only copy of a delete.
    /// </para>
    /// <para>
    /// <b>Never a live function.</b> A queued member whose function has no tombstone is dropped,
    /// not purged: the purge only ever runs against a document that says it was deleted.
    /// </para>
    /// </summary>
    public sealed class FunctionDeletionWorker : BackgroundService
    {
        internal const int DefaultPollSeconds = 10;
        internal const int DefaultPassSeconds = 60;
        internal const int DefaultSettleSeconds = 900;
        internal const int DefaultSweepMinutes = 30;
        internal const int SweepBatchSize = 200;

        /// <summary>Beyond the longest build wait: an invocation or result already in flight.</summary>
        private static readonly TimeSpan SettleMargin = TimeSpan.FromMinutes(5);

        private readonly IDatabase _db;
        private readonly IFunctionRepository _functions;
        private readonly IFunctionPurgeService _purge;
        private readonly IFunctionAuditService _audit;
        private readonly IFunctionTenantSource _tenantSource;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FunctionDeletionWorker> _logger;
        private readonly TimeProvider _time;
        private readonly string _owner = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";

        /// <summary>Per member: consecutive failures and when to try again, so one broken tenant
        /// database is retried with a growing delay rather than on every poll for ever.</summary>
        private readonly Dictionary<string, (int Failures, DateTimeOffset NextAttempt)> _backoff = new(StringComparer.Ordinal);

        private DateTimeOffset _nextSweep = DateTimeOffset.MinValue;

        public FunctionDeletionWorker(
            ICacheClient cache,
            IFunctionRepository functions,
            IFunctionPurgeService purge,
            IFunctionAuditService audit,
            IFunctionTenantSource tenantSource,
            IConfiguration configuration,
            ILogger<FunctionDeletionWorker> logger,
            TimeProvider? time = null)
        {
            ArgumentNullException.ThrowIfNull(cache);
            _db = cache.CacheDatabase();
            _functions = functions;
            _purge = purge;
            _audit = audit;
            _tenantSource = tenantSource;
            _configuration = configuration;
            _logger = logger;
            _time = time ?? TimeProvider.System;
        }

        internal TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Max(2,
            _configuration.GetValue("Functions:DeletionPollSeconds", DefaultPollSeconds)));

        internal TimeSpan PassInterval => TimeSpan.FromSeconds(Math.Max(10,
            _configuration.GetValue("Functions:DeletionPassSeconds", DefaultPassSeconds)));

        internal TimeSpan SweepInterval => TimeSpan.FromMinutes(Math.Max(1,
            _configuration.GetValue("Functions:DeletionSweepMinutes", DefaultSweepMinutes)));

        /// <summary>
        /// Floored at the build wait plus a margin, whatever is configured: a shorter window would
        /// finish a delete while a deploy that started before it could still create a version.
        /// </summary>
        internal TimeSpan SettleWindow
        {
            get
            {
                var configured = TimeSpan.FromSeconds(
                    _configuration.GetValue("Functions:DeletionSettleSeconds", DefaultSettleSeconds));
                var floor = TimeSpan.FromSeconds(_configuration.GetValue("Functions:BuildWaitSeconds", 300)) + SettleMargin;
                return configured > floor ? configured : floor;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("Functions:DeletionWorkerEnabled", true))
            {
                _logger.LogWarning(
                    "The function deletion worker is switched off (Functions:DeletionWorkerEnabled=false); " +
                    "deleted functions stay tombstoned and are not purged");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_time.GetUtcNow() >= _nextSweep)
                    {
                        await SweepTombstonesAsync(stoppingToken);
                        _nextSweep = _time.GetUtcNow() + SweepInterval;
                    }

                    await ProcessPendingAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Housekeeping: never take the Worker down over it.
                    _logger.LogError(ex, "The function deletion worker failed; will retry on the next tick");
                }

                try
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>One look at the queue. Returns how many deletes it finished.</summary>
        internal async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
        {
            var members = await _db.SetMembersAsync(FunctionWorkerQueueKeys.PendingDeletions);
            var finished = 0;

            foreach (var value in members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var member = value.ToString();

                if (!FunctionWorkerQueueKeys.TryParsePendingDeletion(member, out var tenantId, out var functionId))
                {
                    _logger.LogError("Dropping a malformed pending-deletion entry {Member}", member);
                    await _db.SetRemoveAsync(FunctionWorkerQueueKeys.PendingDeletions, value);
                    continue;
                }

                var now = _time.GetUtcNow();
                if (_backoff.TryGetValue(member, out var wait) && now < wait.NextAttempt) continue;

                try
                {
                    if (await ProcessOneAsync(tenantId, functionId, cancellationToken)) finished++;
                    _backoff.Remove(member);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var failures = (_backoff.TryGetValue(member, out var previous) ? previous.Failures : 0) + 1;
                    var delay = TimeSpan.FromSeconds(Math.Min(900, 15 * Math.Pow(2, Math.Min(failures - 1, 6))));
                    _backoff[member] = (failures, now + delay);
                    _logger.LogError(ex,
                        "Purging deleted function {FunctionId} of tenant {TenantId} failed ({Failures} in a row); retrying in {Delay}",
                        functionId, tenantId, failures, delay);
                }
            }

            return finished;
        }

        /// <summary>
        /// One purge pass for one function, if one is due, and the end of the delete if the settle
        /// window has passed. True when the delete is finished and the member removed.
        /// </summary>
        internal async Task<bool> ProcessOneAsync(string tenantId, string functionId, CancellationToken cancellationToken)
        {
            var member = FunctionWorkerQueueKeys.PendingDeletionMember(tenantId, functionId);
            var lockKey = FunctionWorkerQueueKeys.DeletionLock(tenantId, functionId);

            // Another Worker is mid-pass on this function. The purge is idempotent, so this is
            // only about not doing it twice at once and not counting it twice in the tombstone.
            if (!await _db.LockTakeAsync(lockKey, _owner, TimeSpan.FromMinutes(10))) return false;

            try
            {
                var tombstone = await _functions.GetDeletedAsync(tenantId, functionId, cancellationToken);
                if (tombstone?.Deletion is null)
                {
                    // Either finished by another Worker (the document is gone) or — the case that
                    // must never be purged — a live function with a stray entry. Both mean: drop it.
                    if (await _functions.GetByIdAsync(tenantId, functionId, cancellationToken) is not null)
                    {
                        _logger.LogError(
                            "A pending-deletion entry names live function {FunctionId} of tenant {TenantId}; dropping it without purging",
                            functionId, tenantId);
                    }
                    await _db.SetRemoveAsync(FunctionWorkerQueueKeys.PendingDeletions, member);
                    return true;
                }

                var deletion = tombstone.Deletion;
                var startedAt = _time.GetUtcNow().UtcDateTime;
                var settled = startedAt - deletion.RequestedAt >= SettleWindow;

                if (!settled && deletion.LastPassAt is { } last && startedAt - last < PassInterval) return false;

                var report = await _purge.PurgeAsync(tenantId, functionId, cancellationToken);
                await _functions.RecordPurgePassAsync(tenantId, functionId, report, startedAt, cancellationToken);

                if (!settled) return false;

                // This pass began after the settle window: nothing that read the function before
                // its tombstone can still be running, and whatever such work left has just gone.
                await _functions.DeleteTombstoneAsync(tenantId, functionId, cancellationToken);

                await _audit.RecordAsync(
                    tenantId, functionId, FunctionsConstants.AuditActions.Purged,
                    deletion.RequestedBy, deletion.RequestedByEmail,
                    new
                    {
                        Versions = deletion.Versions + report.Versions,
                        Builds = deletion.Builds + report.Builds,
                        Runs = deletion.Runs + report.Runs,
                        RunLogs = deletion.RunLogs + report.RunLogs,
                        ImagesReleased = deletion.ImagesReleased + report.ImagesReleased,
                        RunsCancelled = deletion.RunsCancelled + report.RunsCancelled,
                        Passes = deletion.Passes + 1,
                        deletion.Forced,
                        deletion.RequestedAt,
                    },
                    cancellationToken);

                await _db.SetRemoveAsync(FunctionWorkerQueueKeys.PendingDeletions, member);
                _logger.LogInformation(
                    "Finished deleting function {FunctionId} of tenant {TenantId} after {Passes} purge pass(es)",
                    functionId, tenantId, deletion.Passes + 1);
                return true;
            }
            finally
            {
                try
                {
                    await _db.LockReleaseAsync(lockKey, _owner);
                }
                catch (Exception ex)
                {
                    // It expires on its own; the next pass merely waits for it.
                    _logger.LogWarning("Could not release the deletion lock of {FunctionId}: {Message}", functionId, ex.Message);
                }
            }
        }

        /// <summary>
        /// Re-queues every tombstone in every tenant. The set is the fast path; this is what makes
        /// losing it harmless. Returns how many tombstones it found.
        /// </summary>
        internal async Task<int> SweepTombstonesAsync(CancellationToken cancellationToken)
        {
            bool locked;
            try
            {
                // Not released: held for the interval, so several Workers sweep once between them.
                locked = await _db.LockTakeAsync(FunctionWorkerQueueKeys.DeletionSweepLock, _owner, SweepInterval);
            }
            catch (Exception ex)
            {
                // Without Redis the set cannot be written either; try again next interval.
                _logger.LogWarning("Could not take the deletion sweep lock ({Message}); skipping this sweep", ex.Message);
                return 0;
            }
            if (!locked) return 0;

            var tenants = await _tenantSource.GetActiveTenantIdsAsync(cancellationToken);
            var found = 0;
            foreach (var tenantId in tenants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var ids = await _functions.GetDeletedIdsAsync(tenantId, SweepBatchSize, cancellationToken);
                    foreach (var functionId in ids)
                    {
                        await _db.SetAddAsync(
                            FunctionWorkerQueueKeys.PendingDeletions,
                            FunctionWorkerQueueKeys.PendingDeletionMember(tenantId, functionId));
                        found++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "The deletion sweep failed for tenant {TenantId}; continuing with the rest", tenantId);
                }
            }

            if (found > 0) _logger.LogInformation("The deletion sweep queued {Count} tombstoned function(s)", found);
            return found;
        }
    }
}
