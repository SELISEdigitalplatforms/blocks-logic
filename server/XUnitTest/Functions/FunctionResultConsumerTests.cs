using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Consumers;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using static Functions.DomainService.Consumers.FunctionResultConsumer;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The result consumer trusts nothing on the entry that the run record does not confirm,
    /// never writes a result twice or over a newer attempt, and no longer runs output actions
    /// inline — it hands them to <c>functions:outputs</c> and acknowledges.
    /// </summary>
    public class FunctionResultConsumerTests
    {
        private const string Tenant = "tenant-1";
        private const string RunId = "run-1";

        private readonly Mock<IFunctionRunRepository> _runs = new();
        private readonly Mock<IFunctionRunLogRepository> _logs = new();
        private readonly Mock<IFunctionVersionRepository> _versions = new();
        private readonly Mock<IFunctionImageRecoveryService> _images = new();
        private readonly Mock<IFunctionRepository> _functions = new();
        private readonly (IDatabase Database, FakeRedisDatabase Fake) _redis = FakeRedisDatabase.Create();

        private readonly FunctionVersionEntity _version = new() { ItemId = "v-1", FunctionId = "fn-1" };
        private FunctionRunEntity _record = NewRecord();

        public FunctionResultConsumerTests()
        {
            _runs.Setup(r => r.GetByIdAsync(Tenant, RunId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Clone(_record));
            _runs.Setup(r => r.ApplyResultAsync(
                    Tenant, RunId, It.IsAny<int>(), It.IsAny<RunStatus>(), It.IsAny<RunErrorCode>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<long?>(), It.IsAny<long?>(), It.IsAny<long?>(),
                    It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<RunSandboxReport?>(), It.IsAny<CancellationToken>()))
                .Callback((string _, string _, int _, RunStatus status, RunErrorCode code, string? _, string? result,
                    int? _, long? _, long? _, long? _, string? _, DateTime? _, DateTime _, bool _, RunSandboxReport? _, CancellationToken _) =>
                {
                    _record.Status = status;
                    _record.ErrorCode = code;
                    _record.Result = result;
                })
                .ReturnsAsync(ApplyResultOutcome.Applied);
            _runs.Setup(r => r.TryBeginOutputProcessingAsync(Tenant, RunId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback(() => _record.Status = RunStatus.OutputProcessing)
                .ReturnsAsync(true);
            _versions.Setup(v => v.GetByIdAsync(Tenant, "v-1", It.IsAny<CancellationToken>())).ReturnsAsync(_version);
            _redis.Fake.On("SortedSetAddAsync", _ => true);
        }

        private static FunctionRunEntity NewRecord() => new()
        {
            ItemId = RunId,
            TenantId = Tenant,
            FunctionId = "fn-1",
            VersionId = "v-1",
            Status = RunStatus.Queued,
            Attempt = 1,
            MaxAttempts = 3,
        };

        private static FunctionRunEntity Clone(FunctionRunEntity r) => new()
        {
            ItemId = r.ItemId, TenantId = r.TenantId, FunctionId = r.FunctionId, VersionId = r.VersionId,
            Status = r.Status, ErrorCode = r.ErrorCode, Attempt = r.Attempt, MaxAttempts = r.MaxAttempts,
            Result = r.Result, OutputResults = [.. r.OutputResults],
        };

        private FunctionResultConsumer Consumer()
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_redis.Database);
            return new FunctionResultConsumer(
                cache.Object, _runs.Object, _logs.Object, _versions.Object, _images.Object, _functions.Object,
                NullLogger<FunctionResultConsumer>.Instance);
        }

        private static ResultStreamEntry Entry(params (string Key, string Value)[] overrides)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runId"] = RunId,
                ["tenantId"] = Tenant,
                ["functionId"] = "fn-1",
                ["status"] = FunctionQueueKeys.Wire.Succeeded,
                ["attempt"] = "1",
                ["resultKey"] = FunctionQueueKeys.Result(RunId),
                ["logsKey"] = "",
            };
            foreach (var (k, v) in overrides) fields[k] = v;
            return new ResultStreamEntry("1-0", fields);
        }

        private void VerifyNothingWritten()
        {
            _runs.Verify(r => r.ApplyResultAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<RunStatus>(), It.IsAny<RunErrorCode>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<long?>(), It.IsAny<long?>(), It.IsAny<long?>(),
                It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<RunSandboxReport?>(), It.IsAny<CancellationToken>()), Times.Never);
            _logs.Verify(l => l.InsertManyAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<FunctionRunLogEntity>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private IReadOnlyList<object?[]> OutputJobs() =>
            _redis.Fake.Calls("StreamAddAsync").Where(a => a[0]!.ToString() == FunctionWorkerQueueKeys.OutputsStream).ToList();

        // ---- the happy path, now without inline output actions ------------------------

        [Fact]
        public async Task A_result_is_applied_with_the_attempt_it_reports()
        {
            _record.Attempt = 2;
            _redis.Fake.On("StringGetAsync", _ => (RedisValue)"{\"v\":1}");

            var outcome = await Consumer().ProcessAsync(Entry(("attempt", "2")), CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Applied);
            _runs.Verify(r => r.ApplyResultAsync(
                Tenant, RunId, 2, RunStatus.Succeeded, RunErrorCode.None, It.IsAny<string?>(), "{\"v\":1}",
                It.IsAny<int?>(), It.IsAny<long?>(), It.IsAny<long?>(), It.IsAny<long?>(), It.IsAny<string?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<RunSandboxReport?>(), It.IsAny<CancellationToken>()), Times.Once);
            _redis.Fake.Calls("PublishAsync").Should().ContainSingle();
        }

        [Fact]
        public async Task Output_actions_are_handed_off_not_run_inline()
        {
            _version.OutputActions = [new OutputAction { Id = "a1", Url = "https://example.test" }];

            var outcome = await Consumer().ProcessAsync(Entry(), CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Applied);
            _runs.Verify(r => r.TryBeginOutputProcessingAsync(Tenant, RunId, 1, It.IsAny<CancellationToken>()), Times.Once);
            var job = OutputJobs().Should().ContainSingle().Subject;
            var fields = ((NameValueEntry[])job[1]!).ToDictionary(f => f.Name.ToString(), f => f.Value.ToString());
            fields["runId"].Should().Be(RunId);
            fields["tenantId"].Should().Be(Tenant);
            fields["attempt"].Should().Be("1");
        }

        [Fact]
        public async Task A_success_without_enabled_actions_hands_nothing_off()
        {
            _version.OutputActions = [new OutputAction { Id = "a1", Enabled = false }];

            await Consumer().ProcessAsync(Entry(), CancellationToken.None);

            _runs.Verify(r => r.TryBeginOutputProcessingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            OutputJobs().Should().BeEmpty();
        }

        [Fact]
        public async Task A_retryable_failure_schedules_the_next_attempt_without_moving_an_existing_one()
        {
            await Consumer().ProcessAsync(
                Entry(("status", FunctionQueueKeys.Wire.Failed), ("errorCode", FunctionQueueKeys.Wire.SandboxStartFailed)),
                CancellationToken.None);

            var add = _redis.Fake.Calls("SortedSetAddAsync").Should().ContainSingle().Subject;
            add.Should().Contain(When.NotExists);
            add[1]!.ToString().Should().Contain("\"attempt\":2");
        }

        [Fact]
        public async Task A_user_error_is_not_retried()
        {
            await Consumer().ProcessAsync(
                Entry(("status", FunctionQueueKeys.Wire.Failed), ("errorCode", FunctionQueueKeys.Wire.UserRuntimeError)),
                CancellationToken.None);

            _redis.Fake.Calls("SortedSetAddAsync").Should().BeEmpty();
        }

        // ---- redelivery and attempts ------------------------------------------------------

        [Fact]
        public async Task A_duplicate_delivery_writes_nothing_and_copies_no_logs()
        {
            _record.Status = RunStatus.Failed;
            _record.ErrorCode = RunErrorCode.UserRuntimeError;

            var outcome = await Consumer().ProcessAsync(
                Entry(("status", FunctionQueueKeys.Wire.Succeeded), ("logsKey", FunctionQueueKeys.Logs(RunId))),
                CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Ignored);
            VerifyNothingWritten();
            _redis.Fake.Calls("ListRangeAsync").Should().BeEmpty();
        }

        [Fact]
        public async Task A_duplicate_after_a_crash_before_the_hand_off_finishes_the_hand_off()
        {
            // The first delivery wrote SUCCEEDED and died before handing off the output actions.
            _record.Status = RunStatus.Succeeded;
            _version.OutputActions = [new OutputAction { Id = "a1", Url = "https://example.test" }];

            var outcome = await Consumer().ProcessAsync(Entry(), CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Ignored);
            VerifyNothingWritten();
            OutputJobs().Should().ContainSingle();
        }

        [Fact]
        public async Task A_duplicate_while_output_is_processing_does_not_reapply_the_result()
        {
            _record.Status = RunStatus.OutputProcessing;

            var outcome = await Consumer().ProcessAsync(Entry(), CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Ignored);
            VerifyNothingWritten();
            // Re-offered to the output stream, which never sends an action twice.
            OutputJobs().Should().ContainSingle();
        }

        [Fact]
        public async Task A_result_from_an_older_attempt_never_overwrites_the_current_one()
        {
            _record.Attempt = 2;
            _record.Status = RunStatus.Queued;

            var outcome = await Consumer().ProcessAsync(Entry(("attempt", "1"), ("status", FunctionQueueKeys.Wire.Failed)), CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Ignored);
            VerifyNothingWritten();
            _redis.Fake.Calls("SortedSetAddAsync").Should().BeEmpty();
        }

        [Fact]
        public async Task A_result_for_an_attempt_that_never_started_is_rejected()
        {
            var outcome = await Consumer().ProcessAsync(Entry(("attempt", "3")), CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Rejected);
            VerifyNothingWritten();
        }

        [Theory]
        [InlineData("")]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("one")]
        public async Task A_result_without_a_valid_attempt_is_rejected_rather_than_guessed(string attempt)
        {
            var outcome = await Consumer().ProcessAsync(Entry(("attempt", attempt)), CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Rejected);
            VerifyNothingWritten();
        }

        [Fact]
        public async Task A_real_result_replaces_a_platform_determined_close()
        {
            // The sweeper closed it as Abandoned; the runner's genuine result then arrived.
            _record.Status = RunStatus.Failed;
            _record.ErrorCode = RunErrorCode.Abandoned;

            var outcome = await Consumer().ProcessAsync(Entry(), CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Applied);
        }

        [Fact]
        public async Task A_race_lost_inside_the_write_is_treated_as_a_duplicate()
        {
            _runs.Setup(r => r.ApplyResultAsync(
                    Tenant, RunId, It.IsAny<int>(), It.IsAny<RunStatus>(), It.IsAny<RunErrorCode>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<long?>(), It.IsAny<long?>(), It.IsAny<long?>(),
                    It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<RunSandboxReport?>(), It.IsAny<CancellationToken>()))
                .Callback(() => _record.Status = RunStatus.Succeeded)
                .ReturnsAsync(ApplyResultOutcome.Duplicate);

            var outcome = await Consumer().ProcessAsync(Entry(), CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Ignored);
            _images.Verify(i => i.HandleRunOutcomeAsync(It.IsAny<string>(), It.IsAny<FunctionRunEntity>(), It.IsAny<RunErrorCode?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task A_retry_reset_racing_the_write_is_ignored()
        {
            _runs.Setup(r => r.ApplyResultAsync(
                    Tenant, RunId, It.IsAny<int>(), It.IsAny<RunStatus>(), It.IsAny<RunErrorCode>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<long?>(), It.IsAny<long?>(), It.IsAny<long?>(),
                    It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<RunSandboxReport?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ApplyResultOutcome.StaleAttempt);

            var outcome = await Consumer().ProcessAsync(Entry(), CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Ignored);
            _redis.Fake.Calls("PublishAsync").Should().BeEmpty();
        }

        [Fact]
        public async Task Logs_already_copied_for_the_attempt_are_not_copied_again()
        {
            _redis.Fake.On("KeyExistsAsync", a => a[0]!.ToString() == FunctionWorkerQueueKeys.LogsCopied(RunId, 1));
            // The lines are read beside the marker (one round trip); they must not be inserted.
            _redis.Fake.On("ListRangeAsync", _ => new RedisValue[] { "{\"level\":\"info\",\"msg\":\"hi\"}" });

            await Consumer().ProcessAsync(Entry(("logsKey", FunctionQueueKeys.Logs(RunId))), CancellationToken.None);

            _logs.Verify(l => l.InsertManyAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<FunctionRunLogEntity>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Copied_logs_leave_a_marker()
        {
            _redis.Fake.On("ListRangeAsync", _ => new RedisValue[] { "{\"level\":\"info\",\"msg\":\"hi\"}" });

            await Consumer().ProcessAsync(Entry(("logsKey", FunctionQueueKeys.Logs(RunId))), CancellationToken.None);

            _logs.Verify(l => l.InsertManyAsync(Tenant, It.Is<IReadOnlyList<FunctionRunLogEntity>>(x => x.Count == 1 && x[0].FunctionId == "fn-1"), It.IsAny<CancellationToken>()), Times.Once);
            // Sent without waiting for it (it only guards a redelivery), so it is the synchronous call.
            _redis.Fake.Calls("StringSet").Should().Contain(a => a[0]!.ToString() == FunctionWorkerQueueKeys.LogsCopied(RunId, 1));
        }

        // ---- the entry is not trusted ------------------------------------------------------

        [Fact]
        public async Task A_run_that_does_not_exist_in_the_named_tenant_is_rejected()
        {
            var outcome = await Consumer().ProcessAsync(Entry(("tenantId", "tenant-other")), CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Rejected);
            VerifyNothingWritten();
        }

        [Fact]
        public async Task A_function_id_that_does_not_match_the_run_is_rejected()
        {
            var outcome = await Consumer().ProcessAsync(Entry(("functionId", "fn-other")), CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Rejected);
            VerifyNothingWritten();
        }

        [Fact]
        public async Task A_run_recorded_for_another_tenant_is_rejected()
        {
            _record.TenantId = "tenant-other";

            var outcome = await Consumer().ProcessAsync(Entry(), CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Rejected);
        }

        [Theory]
        [InlineData("resultKey", "function:result:someone-else")]
        [InlineData("resultKey", "blocks-secret-logic")]
        [InlineData("logsKey", "function:logs:someone-else")]
        public async Task Keys_that_are_not_the_runs_own_are_never_read(string field, string key)
        {
            var outcome = await Consumer().ProcessAsync(Entry((field, key)), CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Rejected);
            _redis.Fake.Calls("StringGetAsync").Should().BeEmpty();
            _redis.Fake.Calls("ListRangeAsync").Should().BeEmpty();
            VerifyNothingWritten();
        }

        [Theory]
        [InlineData(FunctionQueueKeys.Wire.Queued)]
        [InlineData(FunctionQueueKeys.Wire.Running)]
        [InlineData(FunctionQueueKeys.Wire.OutputProcessing)]
        public async Task A_non_terminal_status_is_not_a_result(string status)
        {
            var outcome = await Consumer().ProcessAsync(Entry(("status", status)), CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Rejected);
            VerifyNothingWritten();
        }

        [Fact]
        public async Task An_entry_without_run_or_tenant_is_rejected()
        {
            var outcome = await Consumer().ProcessAsync(Entry(("tenantId", "")), CancellationToken.None);

            outcome.Disposition.Should().Be(ResultDisposition.Rejected);
            _runs.Verify(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    
        // ---- warm-sandbox fields (sandbox/REUSE.md) -----------------------------------

        private void VerifySandboxReport(Func<RunSandboxReport?, bool> expected) =>
            _runs.Verify(r => r.ApplyResultAsync(
                Tenant, RunId, It.IsAny<int>(), It.IsAny<RunStatus>(), It.IsAny<RunErrorCode>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<long?>(), It.IsAny<long?>(), It.IsAny<long?>(),
                It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime>(), It.IsAny<bool>(),
                It.Is<RunSandboxReport?>(s => expected(s)), It.IsAny<CancellationToken>()), Times.Once);

        [Fact]
        public async Task The_runners_reuse_fields_are_recorded_on_the_run()
        {
            await Consumer().ProcessAsync(Entry(("reused", "1"), ("discard", "dirty:timer"), ("handoverMs", "4")), CancellationToken.None);

            VerifySandboxReport(s => s == new RunSandboxReport(true, "dirty:timer", 4));
        }

        [Fact]
        public async Task A_fresh_sandbox_that_was_kept_reports_false_and_no_discard()
        {
            await Consumer().ProcessAsync(Entry(("reused", "0"), ("discard", ""), ("handoverMs", "0")), CancellationToken.None);

            VerifySandboxReport(s => s == new RunSandboxReport(false, null, 0));
        }

        [Fact]
        public async Task An_older_runner_without_the_fields_leaves_them_null()
        {
            await Consumer().ProcessAsync(Entry(), CancellationToken.None);

            VerifySandboxReport(s => s == new RunSandboxReport(null, null, null));
        }

        [Theory]
        [InlineData("yes")]
        [InlineData("2")]
        public void An_unrecognised_reused_value_is_not_guessed(string value)
            => SandboxReport(new ResultStreamEntry("1-0", new Dictionary<string, string> { ["reused"] = value, ["handoverMs"] = "x" }))
                .Should().Be(new RunSandboxReport(null, null, null));
}
}
