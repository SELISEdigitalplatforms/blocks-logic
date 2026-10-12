using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Repositories;
using Functions.DomainService.Utils;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The conditional writes are the whole guarantee — a redelivered result, an older attempt, a
    /// racing sweeper or a duplicate output job must change nothing — so they are exercised
    /// against a real MongoDB rather than a mock that would only echo the test's own assumptions.
    /// Needs a local MongoDB: <c>BLOCKS_FUNCTIONS_TEST_MONGO</c> (default
    /// <c>mongodb://localhost:27017</c>). Each run uses, and drops, its own throwaway database;
    /// like the routing fixture, it fails visibly when the prerequisite is absent.
    /// </summary>
    public sealed class FunctionRunRepositoryGuardTests : IDisposable
    {
        private const string Tenant = "tenant-guard";

        private readonly MongoClient _client;
        private readonly string _databaseName = "blocks_fn_guard_" + Guid.NewGuid().ToString("N");
        private readonly IMongoCollection<FunctionRunEntity> _collection;
        private readonly FunctionRunRepository _repository;

        public FunctionRunRepositoryGuardTests()
        {
            var url = Environment.GetEnvironmentVariable("BLOCKS_FUNCTIONS_TEST_MONGO") ?? "mongodb://localhost:27017";
            _client = new MongoClient(url);
            _collection = _client.GetDatabase(_databaseName).GetCollection<FunctionRunEntity>(FunctionsConstants.FunctionRunsCollection);

            var provider = new Mock<IDbContextProvider>();
            provider.Setup(p => p.GetCollection<FunctionRunEntity>(It.IsAny<string>(), It.IsAny<string>())).Returns(_collection);
            _repository = new FunctionRunRepository(provider.Object);
        }

        public void Dispose() => _client.DropDatabase(_databaseName);

        private async Task<FunctionRunEntity> Seed(RunStatus status, int attempt = 1, RunErrorCode code = RunErrorCode.None,
            TimeSpan? age = null, int outputResults = 0)
        {
            var run = new FunctionRunEntity
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow - (age ?? TimeSpan.Zero),
                TenantId = Tenant,
                FunctionId = "fn-1",
                Status = status,
                ErrorCode = code,
                Attempt = attempt,
                OutputResults = Enumerable.Range(0, outputResults).Select(i => new OutputActionResult { ActionId = $"a{i}", Ok = true }).ToList(),
            };
            await _repository.CreateAsync(Tenant, run);
            return run;
        }

        private Task<FunctionRunEntity?> Read(string id) => _repository.GetByIdAsync(Tenant, id);

        private Task<ApplyResultOutcome> Apply(string runId, int attempt, RunStatus status = RunStatus.Succeeded, string? result = "{\"r\":1}") =>
            _repository.ApplyResultAsync(Tenant, runId, attempt, status, RunErrorCode.None, null, result,
                0, 10, null, null, "runner-1", DateTime.UtcNow, DateTime.UtcNow, false);

        // ---- ApplyResultAsync ---------------------------------------------------------

        [Fact]
        public async Task A_result_for_the_current_attempt_is_applied_and_recorded_with_that_attempt()
        {
            var run = await Seed(RunStatus.Queued, attempt: 3);

            (await Apply(run.ItemId, 3)).Should().Be(ApplyResultOutcome.Applied);

            var stored = await Read(run.ItemId);
            stored!.Status.Should().Be(RunStatus.Succeeded);
            stored.Attempts.Should().ContainSingle().Which.Number.Should().Be(3);
        }

        [Fact]
        public async Task A_second_delivery_of_the_same_result_is_a_duplicate_and_pushes_no_second_attempt()
        {
            var run = await Seed(RunStatus.Queued);
            await Apply(run.ItemId, 1);

            (await Apply(run.ItemId, 1, RunStatus.Failed)).Should().Be(ApplyResultOutcome.Duplicate);

            var stored = await Read(run.ItemId);
            stored!.Status.Should().Be(RunStatus.Succeeded);
            stored.Attempts.Should().ContainSingle();
        }

        [Fact]
        public async Task A_result_from_an_older_attempt_never_overwrites_the_current_one()
        {
            var run = await Seed(RunStatus.Queued, attempt: 2);

            (await Apply(run.ItemId, 1, RunStatus.Failed)).Should().Be(ApplyResultOutcome.StaleAttempt);

            (await Read(run.ItemId))!.Status.Should().Be(RunStatus.Queued);
        }

        [Fact]
        public async Task A_result_for_an_attempt_not_yet_started_is_refused()
        {
            var run = await Seed(RunStatus.Queued, attempt: 1);

            (await Apply(run.ItemId, 2)).Should().Be(ApplyResultOutcome.UnknownAttempt);
            (await Apply(run.ItemId, 0)).Should().Be(ApplyResultOutcome.UnknownAttempt);
        }

        [Fact]
        public async Task A_result_for_a_run_that_does_not_exist_is_not_found()
        {
            (await Apply("no-such-run", 1)).Should().Be(ApplyResultOutcome.NotFound);
        }

        [Fact]
        public async Task A_runner_reported_outcome_is_never_replaced()
        {
            var run = await Seed(RunStatus.Failed, code: RunErrorCode.UserRuntimeError);

            (await Apply(run.ItemId, 1)).Should().Be(ApplyResultOutcome.Duplicate);
            (await Read(run.ItemId))!.Status.Should().Be(RunStatus.Failed);
        }

        [Theory]
        [InlineData(RunErrorCode.Abandoned)]
        [InlineData(RunErrorCode.EnqueueFailed)]
        [InlineData(RunErrorCode.Undeliverable)]
        public async Task A_platform_determined_close_gives_way_to_a_real_result(RunErrorCode code)
        {
            var run = await Seed(RunStatus.Failed, code: code);

            (await Apply(run.ItemId, 1)).Should().Be(ApplyResultOutcome.Applied);
            (await Read(run.ItemId))!.Status.Should().Be(RunStatus.Succeeded);
        }

        [Fact]
        public async Task A_result_arriving_during_output_processing_is_a_duplicate()
        {
            var run = await Seed(RunStatus.OutputProcessing);

            (await Apply(run.ItemId, 1, RunStatus.Failed)).Should().Be(ApplyResultOutcome.Duplicate);
            (await Read(run.ItemId))!.Status.Should().Be(RunStatus.OutputProcessing);
        }

        [Fact]
        public async Task The_cpu_window_is_stored_with_its_figure_and_cleared_when_a_later_result_has_none()
        {
            var run = await Seed(RunStatus.Queued, attempt: 1);
            await _repository.ApplyResultAsync(Tenant, run.ItemId, 1, RunStatus.Failed, RunErrorCode.None, null, null,
                1, 1600, null, 12, "runner-1", DateTime.UtcNow, DateTime.UtcNow, false, new RunSandboxReport(null, null, null, 1500));
            (await Read(run.ItemId))!.CpuWindowMs.Should().Be(1500);

            (await _repository.ResetForRetryAsync(Tenant, run.ItemId, 2)).Should().BeTrue();
            (await Read(run.ItemId))!.CpuWindowMs.Should().BeNull("a retry starts with no figures");

            // An older runner's whole-container total: no window may stay next to it.
            await _repository.ApplyResultAsync(Tenant, run.ItemId, 2, RunStatus.Succeeded, RunErrorCode.None, null, null,
                0, 2400, null, 580, "runner-1", DateTime.UtcNow, DateTime.UtcNow, false);
            var stored = await Read(run.ItemId);
            stored!.CpuUsageMs.Should().Be(580);
            stored.CpuWindowMs.Should().BeNull();
        }

        // ---- ResetForRetryAsync ----------------------------------------------------

        [Fact]
        public async Task A_retry_advances_the_run_exactly_once()
        {
            var run = await Seed(RunStatus.Failed, attempt: 1);

            (await _repository.ResetForRetryAsync(Tenant, run.ItemId, 2)).Should().BeTrue();
            (await _repository.ResetForRetryAsync(Tenant, run.ItemId, 2)).Should().BeFalse();

            var stored = await Read(run.ItemId);
            stored!.Attempt.Should().Be(2);
            stored.Status.Should().Be(RunStatus.Queued);
        }

        [Fact]
        public async Task A_run_still_in_flight_is_not_reset()
        {
            var run = await Seed(RunStatus.Queued, attempt: 1);

            (await _repository.ResetForRetryAsync(Tenant, run.ItemId, 2)).Should().BeFalse();
        }

        // ---- output hand-off -----------------------------------------------------------

        [Fact]
        public async Task Output_processing_begins_once_and_its_verdict_is_written_once()
        {
            var run = await Seed(RunStatus.Succeeded);

            (await _repository.TryBeginOutputProcessingAsync(Tenant, run.ItemId, 1)).Should().BeTrue();
            (await _repository.TryBeginOutputProcessingAsync(Tenant, run.ItemId, 1)).Should().BeFalse();

            var results = new List<OutputActionResult> { new() { ActionId = "a1", Ok = false } };
            (await _repository.ApplyOutputResultAsync(Tenant, run.ItemId, 1, results, RunStatus.OutputFailed, "x")).Should().BeTrue();
            (await _repository.ApplyOutputResultAsync(Tenant, run.ItemId, 1, [], RunStatus.Succeeded, null)).Should().BeFalse();

            var stored = await Read(run.ItemId);
            stored!.Status.Should().Be(RunStatus.OutputFailed);
            stored.ErrorCode.Should().Be(RunErrorCode.OutputActionFailed);
            stored.OutputResults.Should().ContainSingle();
        }

        [Fact]
        public async Task A_run_whose_output_already_finished_does_not_begin_again()
        {
            var run = await Seed(RunStatus.Succeeded, outputResults: 1);

            (await _repository.TryBeginOutputProcessingAsync(Tenant, run.ItemId, 1)).Should().BeFalse();
        }

        [Fact]
        public async Task An_output_verdict_for_another_attempt_is_refused()
        {
            var run = await Seed(RunStatus.OutputProcessing, attempt: 2);

            (await _repository.ApplyOutputResultAsync(Tenant, run.ItemId, 1, [], RunStatus.Succeeded, null)).Should().BeFalse();
        }

        // ---- the sweeper's reads and compare-and-set --------------------------------------

        [Fact]
        public async Task Stale_candidates_are_only_the_old_runs_in_the_asked_statuses()
        {
            var old = await Seed(RunStatus.Queued, age: TimeSpan.FromHours(2));
            await Seed(RunStatus.Queued, age: TimeSpan.FromMinutes(1));
            await Seed(RunStatus.Failed, age: TimeSpan.FromHours(2));

            var found = await _repository.FindStaleCandidatesAsync(
                Tenant, [RunStatus.Queued], DateTime.UtcNow.AddHours(-1), null, 10);

            found.Select(r => r.ItemId).Should().Equal(old.ItemId);
        }

        [Fact]
        public async Task A_stale_close_lands_when_nothing_changed()
        {
            var run = await Seed(RunStatus.Queued, age: TimeSpan.FromHours(7));
            var read = (await Read(run.ItemId))!;

            (await _repository.CloseStaleAsync(Tenant, run.ItemId, read.Status, read.Attempt, read.LastUpdatedDate,
                RunStatus.Failed, RunErrorCode.Abandoned, "lost", DateTime.UtcNow)).Should().BeTrue();

            var stored = await Read(run.ItemId);
            stored!.Status.Should().Be(RunStatus.Failed);
            stored.ErrorCode.Should().Be(RunErrorCode.Abandoned);
        }

        [Fact]
        public async Task A_result_that_raced_in_after_the_sweepers_read_wins()
        {
            var run = await Seed(RunStatus.Queued, age: TimeSpan.FromHours(7));
            var read = (await Read(run.ItemId))!;

            await Apply(run.ItemId, 1);

            (await _repository.CloseStaleAsync(Tenant, run.ItemId, read.Status, read.Attempt, read.LastUpdatedDate,
                RunStatus.Failed, RunErrorCode.Abandoned, "lost", DateTime.UtcNow)).Should().BeFalse();
            (await Read(run.ItemId))!.Status.Should().Be(RunStatus.Succeeded);
        }

        [Fact]
        public async Task A_retry_reset_after_the_sweepers_read_is_not_closed()
        {
            var run = await Seed(RunStatus.Failed, age: TimeSpan.FromHours(7));
            var read = (await Read(run.ItemId))!;

            // Re-queued for attempt 2 after the read: status and timestamp both moved on, so a
            // close built from the old read — whatever status it expects — must not land.
            await _repository.ResetForRetryAsync(Tenant, run.ItemId, 2);

            (await _repository.CloseStaleAsync(Tenant, run.ItemId, RunStatus.Queued, 1, read.LastUpdatedDate,
                RunStatus.Failed, RunErrorCode.Abandoned, "lost", DateTime.UtcNow)).Should().BeFalse();
            (await Read(run.ItemId))!.Status.Should().Be(RunStatus.Queued);
        }
    }
}
