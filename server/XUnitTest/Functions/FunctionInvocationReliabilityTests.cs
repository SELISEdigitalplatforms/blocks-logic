using System.Diagnostics;
using Blocks.Genesis;
using Common.InternalService.Access;
using FluentAssertions;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The invoke path's failure modes: a run whose enqueue fails must be closed and reported as
    /// 503 rather than left QUEUED for ever, a caller disconnecting must never strand a half-queued
    /// run, and the synchronous wait must be bounded, woken by the completion notification,
    /// cheap on the database while it waits, and cancellable.
    /// </summary>
    public class FunctionInvocationReliabilityTests
    {
        private const string Tenant = "tenant-1";

        private readonly Mock<IFunctionRepository> _functions = new();
        private readonly Mock<IFunctionVersionRepository> _versions = new();
        private readonly Mock<IFunctionRunRepository> _runs = new();
        private readonly Mock<IFunctionAdmissionService> _admission = new();
        private readonly Mock<IFunctionAuthorizationService> _authorization = new();
        private readonly Mock<IFunctionBuildService> _builds = new();
        private readonly Mock<IFunctionDelegationService> _delegation = new();
        private readonly Mock<global::Functions.DomainService.Storage.IFunctionArtifactStore> _artifacts = new();
        private readonly Mock<ISubscriber> _subscriber = new();
        private readonly (IDatabase Database, FakeRedisDatabase Fake) _redis = FakeRedisDatabase.Create();

        private readonly FunctionEntity _function = new()
        {
            ItemId = "fn-1",
            Status = FunctionStatus.Live,
            ActiveVersionId = "v-1",
        };

        private readonly FunctionVersionEntity _version = new()
        {
            ItemId = "v-1",
            FunctionId = "fn-1",
            Number = 1,
            ImageDigest = "sha256:abc",
        };

        private FunctionRunEntity? _created;
        private Action<RedisChannel, RedisValue>? _notify;

        public FunctionInvocationReliabilityTests()
        {
            _version.Limits.TimeoutSeconds = 60;
            _function.Limits.TimeoutSeconds = 60;

            _functions.Setup(f => f.GetByIdAsync(Tenant, "fn-1", It.IsAny<CancellationToken>())).ReturnsAsync(_function);
            _versions.Setup(v => v.GetByIdAsync(Tenant, "v-1", It.IsAny<CancellationToken>())).ReturnsAsync(_version);
            _authorization
                .Setup(a => a.AuthorizeForWorkflow(It.IsAny<FunctionEntity>(), It.IsAny<FunctionVersionEntity?>(), It.IsAny<BlocksContext?>()))
                .Returns(new AuthorizationResult(true));
            _admission
                .Setup(a => a.AdmitAsync(It.IsAny<FunctionEntity>(), Tenant, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdmissionResult.Admit());
            _runs
                .Setup(r => r.CreateAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<CancellationToken>()))
                .Callback((string _, FunctionRunEntity run, CancellationToken _) => _created = run)
                .Returns(Task.CompletedTask);
            _builds
                .Setup(b => b.EnsureImageAsync(Tenant, It.IsAny<FunctionEntity>(), It.IsAny<CancellationToken>(), It.IsAny<int?>(), It.IsAny<bool>()))
                .ReturnsAsync(new FunctionBuildEntity { ItemId = "b-1", Status = BuildStatus.Succeeded, ImageDigest = "sha256:test" });
            _builds
                .Setup(b => b.CreateTestBuildAsync(Tenant, It.IsAny<FunctionEntity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((new FunctionBuildEntity { ItemId = "tb-1", Status = BuildStatus.Queued, Ephemeral = true }, "function:source:tb-1"));

            var multiplexer = new Mock<IConnectionMultiplexer>();
            multiplexer.Setup(m => m.GetSubscriber(It.IsAny<object?>())).Returns(_subscriber.Object);
            _redis.Fake.On("get_Multiplexer", _ => multiplexer.Object);

            // The test rate limiter takes this key before anything else happens. True means "you
            // are the first in the window", which is what every case here assumes; the limiter has
            // its own tests.
            _redis.Fake.On("StringSetAsync", _ => true);
            _subscriber
                .Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                .Callback((RedisChannel _, Action<RedisChannel, RedisValue> handler, CommandFlags _) => _notify = handler)
                .Returns(Task.CompletedTask);
        }

        private FunctionInvocationService Service(params (string Key, string Value)[] settings)
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_redis.Database);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
                .Build();

            return new FunctionInvocationService(
                _functions.Object, _versions.Object, _runs.Object, _admission.Object,
                _authorization.Object, _builds.Object, new Mock<IEndpointAccessAuthorizer>().Object,
                _delegation.Object, new HttpContextAccessor(), _artifacts.Object, cache.Object, configuration,
                NullLogger<FunctionInvocationService>.Instance);
        }

        private void RunIs(Func<FunctionRunEntity?> current) =>
            _runs.Setup(r => r.GetByIdAsync(Tenant, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => current());

        private static FunctionRunEntity Run(RunStatus status) => new() { ItemId = "run", Status = status, Result = "{\"ok\":true}" };

        // ---- the active version is kept in memory, and a new deploy is seen at once --

        [Fact]
        public async Task The_active_version_is_read_from_mongo_once_across_calls()
        {
            var service = Service();

            await service.InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1");
            await service.InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1");

            _versions.Verify(v => v.GetByIdAsync(Tenant, "v-1", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_new_deploy_is_used_on_the_very_next_call()
        {
            var service = Service();
            await service.InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1");
            _created!.VersionId.Should().Be("v-1");

            var v2 = new FunctionVersionEntity { ItemId = "v-2", FunctionId = "fn-1", Number = 2, ImageDigest = "sha256:def" };
            v2.Limits.TimeoutSeconds = 60;
            _versions.Setup(v => v.GetByIdAsync(Tenant, "v-2", It.IsAny<CancellationToken>())).ReturnsAsync(v2);
            _function.ActiveVersionId = "v-2";

            await service.InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1");

            _created!.VersionId.Should().Be("v-2");
            _created.VersionNumber.Should().Be(2);
        }

        // ---- secret references stay references in the queue -----------------------

        /// <summary>
        /// Everything the invoke path wrote to Redis for one run — the run-record hash and the
        /// stream entry — flattened to text, so an assertion can cover all of it at once.
        /// </summary>
        private string EverythingEnqueued()
        {
            var parts = new List<string>();
            foreach (var call in _redis.Fake.Calls("HashSetAsync"))
            {
                if (call[1] is HashEntry[] entries) parts.AddRange(entries.Select(e => $"{e.Name}={e.Value}"));
            }
            foreach (var call in _redis.Fake.Calls("StreamAddAsync"))
            {
                if (call[1] is NameValueEntry[] entries) parts.AddRange(entries.Select(e => $"{e.Name}={e.Value}"));
            }
            return string.Join("\n", parts);
        }

        private async Task InvokeUntilQueuedAsync()
        {
            // The wait is abandoned as soon as the record exists; the enqueue still completes,
            // which is the part under test.
            using var cts = new CancellationTokenSource();
            _runs
                .Setup(r => r.CreateAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<CancellationToken>()))
                .Callback((string _, FunctionRunEntity run, CancellationToken _) =>
                {
                    _created = run;
                    cts.Cancel();
                })
                .Returns(Task.CompletedTask);

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1", cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task The_queued_payload_carries_secret_references_and_never_a_resolved_value()
        {
            _version.Variables =
            [
                new VariableBinding { Key = "STRIPE_API_KEY", Value = "{{secret.sec_stripe}}" },
                new VariableBinding { Key = "AUTH", Value = "Bearer {{secret.sec_token}}" },
                new VariableBinding { Key = "API_BASE", Value = "https://api.example.com" },
            ];

            await InvokeUntilQueuedAsync();

            var enqueued = EverythingEnqueued();
            enqueued.Should().Contain("{{secret.sec_stripe}}").And.Contain("Bearer {{secret.sec_token}}");

            using var envelope = System.Text.Json.JsonDocument.Parse(
                _redis.Fake.Calls("HashSetAsync").SelectMany(c => (HashEntry[])c[1]!)
                    .Single(e => e.Name == "envelope").Value.ToString());
            envelope.RootElement.GetProperty("env").GetProperty("STRIPE_API_KEY").GetString()
                .Should().Be("{{secret.sec_stripe}}");
            envelope.RootElement.GetProperty("maskedEnv").EnumerateArray().Select(e => e.GetString())
                .Should().BeEquivalentTo(["STRIPE_API_KEY", "AUTH"]);
            envelope.RootElement.TryGetProperty("maskedValues", out _).Should().BeFalse();
        }

        [Fact]
        public async Task The_run_entry_announces_the_protocol_an_old_runner_refuses()
        {
            // An old runner dead-letters anything but protocol 1, which is the guard against it
            // executing a function with "{{secret.x}}" where its key should be.
            await InvokeUntilQueuedAsync();

            var entry = _redis.Fake.Calls("StreamAddAsync").Single()[1] as NameValueEntry[];
            entry!.Single(e => e.Name == "protocol").Value.ToString()
                .Should().Be(FunctionQueueKeys.RunProtocolVersion.ToString());
            FunctionQueueKeys.RunProtocolVersion.Should().BeGreaterThan(FunctionQueueKeys.ProtocolVersion);
        }

        // ---- the artifact a runner needs to build the image locally ------------------

        private const string ArtifactSas = "https://acct.blob.core.windows.net/blocks-fn-artifacts/t1/b1.tar?sig=x";

        /// <summary>
        /// A signed, read-only URL and the hash to check it against, so a host that has never seen
        /// this function can fetch what it needs and know it got the right bytes.
        /// </summary>
        [Fact]
        public async Task A_version_with_an_artifact_queues_its_url_and_its_hash()
        {
            _version.ArtifactId = "build_1";
            _version.ArtifactSha256 = "deadbeef";
            _artifacts
                .Setup(a => a.CreateDownloadUrlAsync(Tenant, "build_1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArtifactSas);

            await InvokeUntilQueuedAsync();

            var entry = _redis.Fake.Calls("StreamAddAsync").Single()[1] as NameValueEntry[];
            entry!.Single(e => e.Name == FunctionQueueKeys.RunArtifactUrlField).Value.ToString()
                .Should().Be(ArtifactSas);
            entry.Single(e => e.Name == FunctionQueueKeys.RunArtifactSha256Field).Value.ToString()
                .Should().Be("deadbeef");
        }

        /// <summary>
        /// An artifact-only build has no registry digest. The run must still name an image — the
        /// runner builds the artifact under that name and dead-letters an entry without one
        /// ("the entry carries no image"), which is what every such run used to hit.
        /// </summary>
        [Fact]
        public async Task An_artifact_only_version_queues_a_local_image_name_for_the_runner_to_build()
        {
            _version.ImageDigest = string.Empty;
            _version.ArtifactId = "Build_1";
            _version.ArtifactSha256 = "deadbeef";
            _artifacts
                .Setup(a => a.CreateDownloadUrlAsync(Tenant, "Build_1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArtifactSas);

            await InvokeUntilQueuedAsync();

            var entry = _redis.Fake.Calls("StreamAddAsync").Single()[1] as NameValueEntry[];
            entry!.Single(e => e.Name == "image").Value.ToString()
                .Should().Be("blocks-fn-artifact/build_1:local");
            entry.Single(e => e.Name == FunctionQueueKeys.RunArtifactUrlField).Value.ToString()
                .Should().Be(ArtifactSas);
        }

        /// <summary>
        /// The old path, untouched. A runner that predates these fields must see exactly what it saw
        /// before, which is what lets both run side by side through the cutover.
        /// </summary>
        [Fact]
        public async Task A_version_without_an_artifact_queues_neither_field()
        {
            _version.ArtifactId = null;

            await InvokeUntilQueuedAsync();

            var entry = _redis.Fake.Calls("StreamAddAsync").Single()[1] as NameValueEntry[];
            entry!.Should().NotContain(e => e.Name == FunctionQueueKeys.RunArtifactUrlField);
            entry.Should().NotContain(e => e.Name == FunctionQueueKeys.RunArtifactSha256Field);
            _artifacts.Verify(
                a => a.CreateDownloadUrlAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// The artifact is gone from the store. Queueing a half-described run would hand a host a URL
        /// to nothing; the entry falls back to the image reference instead and the run still goes.
        /// </summary>
        [Fact]
        public async Task An_artifact_missing_from_the_store_does_not_queue_an_empty_url()
        {
            _version.ArtifactId = "build_gone";
            _artifacts
                .Setup(a => a.CreateDownloadUrlAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);

            await InvokeUntilQueuedAsync();

            var entry = _redis.Fake.Calls("StreamAddAsync").Single()[1] as NameValueEntry[];
            entry!.Should().NotContain(e => e.Name == FunctionQueueKeys.RunArtifactUrlField);
            entry.Single(e => e.Name == "image").Value.ToString().Should().NotBeEmpty();
        }

        /// <summary>
        /// Tenant storage that cannot serve the artifact (none, SFTP, failed to open) must not take
        /// runs down with it — a run may be a workflow step. It goes by image reference, like a
        /// missing artifact.
        /// </summary>
        [Fact]
        public async Task Unavailable_tenant_storage_queues_the_run_by_image_reference()
        {
            _version.ArtifactId = "build_1";
            _artifacts
                .Setup(a => a.CreateDownloadUrlAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new global::Functions.DomainService.Storage.FunctionArtifactStoreUnavailableException("unavailable"));

            await InvokeUntilQueuedAsync();

            var entry = _redis.Fake.Calls("StreamAddAsync").Single()[1] as NameValueEntry[];
            entry!.Should().NotContain(e => e.Name == FunctionQueueKeys.RunArtifactUrlField);
            entry.Single(e => e.Name == "image").Value.ToString().Should().NotBeEmpty();
        }

        [Fact]
        public async Task A_reference_to_a_secret_that_may_not_exist_is_queued_not_refused()
        {
            // No secret store is consulted at invoke any more; the runner fails such a run as
            // SecretUnresolved, naming the variable, before its sandbox starts.
            _version.Variables = [new VariableBinding { Key = "TOKEN", Value = "{{secret.deleted_long_ago}}" }];

            await InvokeUntilQueuedAsync();

            _created.Should().NotBeNull();
            _redis.Fake.Calls("StreamAddAsync").Should().ContainSingle();
        }

        // ---- ctx.blocks.accessToken: a grant id beside the envelope ------------------

        private static readonly string Grant = "dg_" + new string('c', 64);

        private static readonly BlocksContext Caller = BlocksContext.Create(
            tenantId: Tenant, roles: ["dev"], userId: "user-1", isAuthenticated: true, requestUri: string.Empty,
            organizationId: "org-1", expireOn: DateTime.UtcNow.AddMinutes(5), email: "u@example.com", permissions: [],
            userName: string.Empty, phoneNumber: string.Empty, displayName: string.Empty, oauthToken: "eyJ.caller.token",
            originalTenantId: Tenant, applicationDomain: string.Empty, impersonated: false, impersonationSessionId: string.Empty);

        private void GrantsAre(string? grant) =>
            _delegation
                .Setup(d => d.CreateGrantAsync(It.IsAny<string>(), It.IsAny<BlocksContext?>(), It.IsAny<AuthMode>()))
                .ReturnsAsync(grant);

        [Fact]
        public async Task The_callers_grant_id_is_queued_beside_the_envelope_and_never_inside_it()
        {
            GrantsAre(Grant);
            _version.Trigger.AuthMode = AuthMode.Token;
            using var cts = new CancellationTokenSource();
            _runs
                .Setup(r => r.CreateAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<CancellationToken>()))
                .Callback((string _, FunctionRunEntity run, CancellationToken _) => { _created = run; cts.Cancel(); })
                .Returns(Task.CompletedTask);

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", Caller, null, "wf-1", cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();

            _delegation.Verify(d => d.CreateGrantAsync(Tenant, Caller, AuthMode.Token), Times.Once);
            var hash = _redis.Fake.Calls("HashSetAsync").SelectMany(c => (HashEntry[])c[1]!).ToList();
            hash.Should().ContainSingle(e => e.Name == FunctionQueueKeys.RunDelegationField)
                .Which.Value.ToString().Should().Be(Grant);
            hash.Single(e => e.Name == "envelope").Value.ToString().Should().NotContain(Grant).And.NotContain("eyJ.caller.token");
            var entry = (NameValueEntry[])_redis.Fake.Calls("StreamAddAsync").Single()[1]!;
            entry.Should().NotContain(e => e.Value.ToString() == Grant, "the stream entry is not where the runner looks");
            EverythingEnqueued().Should().NotContain("eyJ.caller.token", "the caller's own token never enters the queue");
        }

        [Fact]
        public async Task A_version_whose_code_never_names_the_token_gets_no_grant()
        {
            GrantsAre(Grant);
            _version.Trigger.AuthMode = AuthMode.Token;
            _version.Source.IndexJs = "export default async function handler(ctx) { return { ok: ctx.input }; }";
            using var cts = new CancellationTokenSource();
            _runs
                .Setup(r => r.CreateAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<CancellationToken>()))
                .Callback((string _, FunctionRunEntity run, CancellationToken _) => { _created = run; cts.Cancel(); })
                .Returns(Task.CompletedTask);

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", Caller, null, "wf-1", cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();

            _delegation.Verify(
                d => d.CreateGrantAsync(It.IsAny<string>(), It.IsAny<BlocksContext?>(), It.IsAny<AuthMode>()), Times.Never);
            _redis.Fake.Calls("HashSetAsync").SelectMany(c => (HashEntry[])c[1]!)
                .Should().NotContain(e => e.Name == FunctionQueueKeys.RunDelegationField);
        }

        [Fact]
        public async Task A_version_whose_code_reads_the_token_still_gets_its_grant()
        {
            GrantsAre(Grant);
            _version.Trigger.AuthMode = AuthMode.Token;
            _version.Source.IndexJs = "export default async (ctx) => ctx.blocks.accessToken ? 1 : 0;";
            using var cts = new CancellationTokenSource();
            _runs
                .Setup(r => r.CreateAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<CancellationToken>()))
                .Callback((string _, FunctionRunEntity run, CancellationToken _) => { _created = run; cts.Cancel(); })
                .Returns(Task.CompletedTask);

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", Caller, null, "wf-1", cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();

            _delegation.Verify(d => d.CreateGrantAsync(Tenant, Caller, AuthMode.Token), Times.Once);
        }

        [Fact]
        public async Task A_run_without_a_grant_queues_no_delegation_field()
        {
            GrantsAre(null);

            await InvokeUntilQueuedAsync();

            _redis.Fake.Calls("HashSetAsync").SelectMany(c => (HashEntry[])c[1]!)
                .Should().NotContain(e => e.Name == FunctionQueueKeys.RunDelegationField);
        }

        [Fact]
        public async Task A_failed_enqueue_deletes_the_grant_it_will_never_use()
        {
            GrantsAre(Grant);
            _redis.Fake.On("StreamAddAsync", _ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", Caller, null, "wf-1");

            await act.Should().ThrowAsync<FunctionUnavailableException>();
            _delegation.Verify(d => d.DeleteGrantAsync(Grant), Times.Once);
        }

        [Fact]
        public async Task A_caller_gone_before_the_record_leaves_no_grant_behind()
        {
            GrantsAre(Grant);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", Caller, null, "wf-1", cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            _created.Should().BeNull();
            _delegation.Verify(d => d.DeleteGrantAsync(Grant), Times.Once);
        }

        [Fact]
        public async Task A_successful_enqueue_keeps_its_grant_for_the_runner()
        {
            GrantsAre(Grant);

            await InvokeUntilQueuedAsync();

            _delegation.Verify(d => d.DeleteGrantAsync(It.IsAny<string?>()), Times.Never);
        }

        // ---- enqueue compensation -------------------------------------------------

        [Fact]
        public async Task A_failed_enqueue_closes_the_run_and_answers_unavailable()
        {
            _redis.Fake.On("StreamAddAsync", _ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1");

            var thrown = await act.Should().ThrowAsync<FunctionUnavailableException>();
            thrown.Which.RunId.Should().Be(_created!.ItemId);

            _runs.Verify(r => r.FailIfNotTerminalAsync(
                Tenant, _created.ItemId, RunErrorCode.EnqueueFailed,
                It.Is<string>(m => m.Contains("nothing was executed", StringComparison.Ordinal)),
                It.IsAny<DateTime>(), CancellationToken.None), Times.Once);

            // The payload is withdrawn, so an entry that did land cannot run it.
            _redis.Fake.Calls("KeyDeleteAsync")
                .Should().ContainSingle(a => a[0]!.ToString() == FunctionQueueKeys.Run(_created.ItemId));

            // Counted like every other run that has a record.
            _functions.Verify(s => s.RecordRunStartedAsync(Tenant, "fn-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

            // No waiting on a run that was never queued.
            _runs.Verify(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task A_payload_write_failure_is_compensated_the_same_way()
        {
            _redis.Fake.On("HashSetAsync", _ => throw new RedisTimeoutException("timeout", CommandStatus.Unknown));

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1");

            await act.Should().ThrowAsync<FunctionUnavailableException>();
            _runs.Verify(r => r.FailIfNotTerminalAsync(
                Tenant, It.IsAny<string>(), RunErrorCode.EnqueueFailed, It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
            _redis.Fake.Calls("StreamAddAsync").Should().BeEmpty();
        }

        [Fact]
        public async Task The_caller_still_gets_503_when_closing_the_run_fails_too()
        {
            // Mongo down as well: the sweeper closes the record later, but the caller must still
            // be told "unavailable", not handed the Mongo exception as a 500.
            _redis.Fake.On("StreamAddAsync", _ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));
            _redis.Fake.On("KeyDeleteAsync", _ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));
            _runs.Setup(r => r.FailIfNotTerminalAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RunErrorCode>(), It.IsAny<string>(),
                    It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("mongo"));

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1");

            await act.Should().ThrowAsync<FunctionUnavailableException>();
        }

        [Fact]
        public async Task A_caller_disconnecting_after_the_record_exists_cannot_strand_it()
        {
            using var cts = new CancellationTokenSource();
            _runs
                .Setup(r => r.CreateAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<CancellationToken>()))
                .Callback((string _, FunctionRunEntity run, CancellationToken _) =>
                {
                    _created = run;
                    cts.Cancel();
                })
                .Returns(Task.CompletedTask);

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1", cts.Token);

            // The wait is abandoned — the caller has gone — but the run was fully queued first.
            await act.Should().ThrowAsync<OperationCanceledException>();
            _redis.Fake.Calls("StreamAddAsync").Should().ContainSingle();
            _runs.Verify(r => r.CreateAsync(Tenant, It.IsAny<FunctionRunEntity>(), CancellationToken.None), Times.Once);
        }

        [Fact]
        public async Task A_caller_already_gone_before_the_record_creates_nothing()
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1", cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            _created.Should().BeNull();
            _redis.Fake.Calls("StreamAddAsync").Should().BeEmpty();
        }

        // ---- synchronous wait ---------------------------------------------------------

        [Fact]
        public async Task A_terminal_run_is_returned_with_its_result()
        {
            RunIs(() => Run(RunStatus.Succeeded));

            var result = await Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1");

            result.Status.Should().Be(FunctionQueueKeys.Wire.Succeeded);
            result.Result.Should().Be("{\"ok\":true}");
        }

        [Fact]
        public async Task The_http_facing_wait_is_capped_low_and_returns_the_run_id_to_poll()
        {
            RunIs(() => Run(RunStatus.Queued));

            var stopwatch = Stopwatch.StartNew();
            var result = await Service(("Functions:HttpSyncWaitMaxSeconds", "1"), ("Functions:SyncWaitMaxSeconds", "180"))
                .TestAsync(Tenant, "fn-1", new TestFunctionRequestDto { FunctionId = "fn-1", InputJson = "{}" });
            stopwatch.Stop();

            result.Status.Should().Be(FunctionQueueKeys.Wire.Queued);
            result.RunId.Should().Be(_created!.ItemId);
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(4));
        }

        /// <summary>
        /// The Test button always has a signed-in user, and ctx.context already carries them, so a
        /// Public trigger must not leave the editor's run without ctx.blocks.accessToken.
        /// </summary>
        [Fact]
        public async Task A_test_of_a_public_function_still_asks_for_the_callers_grant()
        {
            RunIs(() => Run(RunStatus.Queued));
            GrantsAre(Grant);
            _function.Trigger.AuthMode = AuthMode.Public;

            await Service(("Functions:HttpSyncWaitMaxSeconds", "1"))
                .TestAsync(Tenant, "fn-1", new TestFunctionRequestDto { FunctionId = "fn-1", InputJson = "{}" });

            _delegation.Verify(
                d => d.CreateGrantAsync(Tenant, It.IsAny<BlocksContext?>(), AuthMode.Token), Times.Once);
            _delegation.Verify(
                d => d.CreateGrantAsync(It.IsAny<string>(), It.IsAny<BlocksContext?>(), AuthMode.Public), Times.Never);
        }

        [Fact]
        public async Task A_workflow_run_of_a_public_function_keeps_the_public_auth_mode()
        {
            GrantsAre(null);
            _version.Trigger.AuthMode = AuthMode.Public;
            using var cts = new CancellationTokenSource();
            _runs
                .Setup(r => r.CreateAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<CancellationToken>()))
                .Callback((string _, FunctionRunEntity run, CancellationToken _) => { _created = run; cts.Cancel(); })
                .Returns(Task.CompletedTask);

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", Caller, null, "wf-1", cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();

            _delegation.Verify(d => d.CreateGrantAsync(Tenant, Caller, AuthMode.Public), Times.Once);
        }

        /// <summary>
        /// A test builds and runs on a host that is also serving deployed functions, so one costs
        /// real capacity. Refused rather than queued, because the developer is watching: "wait a
        /// moment" is a better answer than a run that surfaces minutes later.
        /// </summary>
        [Fact]
        public async Task Testing_the_same_function_twice_in_the_window_is_refused_not_queued()
        {
            RunIs(() => Run(RunStatus.Queued));
            _redis.Fake.On("StringSetAsync", _ => false);          // the window is already held
            _redis.Fake.On("KeyTimeToLiveAsync", _ => TimeSpan.FromSeconds(45));

            var act = async () => await Service()
                .TestAsync(Tenant, "fn-1", new TestFunctionRequestDto { FunctionId = "fn-1", InputJson = "{}" });

            var thrown = await act.Should().ThrowAsync<FunctionRateLimitedException>();
            thrown.Which.RetryAfterSeconds.Should().Be(45, "the caller is told when, not just no");
            _redis.Fake.Calls("StreamAddAsync").Should().BeEmpty("nothing is queued for a refused test");
        }

        [Fact]
        public async Task The_window_is_one_minute()
        {
            FunctionQueueKeys.TestRateWindow.Should().Be(TimeSpan.FromSeconds(60));
            await Task.CompletedTask;
        }

        [Fact]
        public async Task A_test_is_one_job_on_the_tests_stream_with_its_own_build()
        {
            // Build and run on the same runner, so no other host is ever asked for the image.
            RunIs(() => Run(RunStatus.Queued));

            var result = await Service(("Functions:HttpSyncWaitMaxSeconds", "1"))
                .TestAsync(Tenant, "fn-1", new TestFunctionRequestDto { FunctionId = "fn-1", InputJson = "{}" });

            var call = _redis.Fake.Calls("StreamAddAsync").Single();
            call[0].ToString().Should().Be(FunctionQueueKeys.TestsStream);
            var entry = (NameValueEntry[])call[1]!;
            entry.Single(e => e.Name == "buildId").Value.ToString().Should().Be("tb-1");
            entry.Single(e => e.Name == "sourceKey").Value.ToString().Should().Be("function:source:tb-1");
            entry.Single(e => e.Name == "runId").Value.ToString().Should().Be(_created!.ItemId);
            result.BuildId.Should().Be("tb-1");
        }

        [Fact]
        public async Task A_test_never_reuses_a_cached_build()
        {
            RunIs(() => Run(RunStatus.Queued));

            await Service(("Functions:HttpSyncWaitMaxSeconds", "1"))
                .TestAsync(Tenant, "fn-1", new TestFunctionRequestDto { FunctionId = "fn-1", InputJson = "{}", Rebuild = false });

            _builds.Verify(b => b.EnsureImageAsync(
                It.IsAny<string>(), It.IsAny<FunctionEntity>(), It.IsAny<CancellationToken>(), It.IsAny<int?>(), It.IsAny<bool>()),
                Times.Never);
            _builds.Verify(b => b.CreateTestBuildAsync(Tenant, It.IsAny<FunctionEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_test_is_never_retried_because_its_image_is_gone_afterwards()
        {
            RunIs(() => Run(RunStatus.Queued));

            await Service(("Functions:HttpSyncWaitMaxSeconds", "1"))
                .TestAsync(Tenant, "fn-1", new TestFunctionRequestDto { FunctionId = "fn-1", InputJson = "{}" });

            _created!.MaxAttempts.Should().Be(1);
            _created.ImageDigest.Should().Be("blocks-test/tb-1:local");
        }

        [Fact]
        public void The_default_http_cap_sits_under_a_60_second_ingress_timeout()
        {
            FunctionInvocationService.DefaultHttpSyncWaitMaxSeconds.Should().BeLessThanOrEqualTo(30);
        }

        [Fact]
        public async Task A_workflow_wait_is_not_held_to_the_http_cap()
        {
            // The workflow step runs in the Worker with no ingress in front of it. Queued for
            // ~1.5 s, then done: an HTTP cap of 1 s would have given up.
            var sw = Stopwatch.StartNew();
            RunIs(() => sw.Elapsed > TimeSpan.FromMilliseconds(1500) ? Run(RunStatus.Succeeded) : Run(RunStatus.Queued));

            var result = await Service(("Functions:HttpSyncWaitMaxSeconds", "1"), ("Functions:SyncWaitMaxSeconds", "10"))
                .InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, 5, "wf-1");

            result.Status.Should().Be(FunctionQueueKeys.Wire.Succeeded);
        }

        [Fact]
        public async Task The_completion_notification_wakes_the_wait_before_the_next_poll()
        {
            var done = false;
            RunIs(() => done ? Run(RunStatus.Succeeded) : Run(RunStatus.Queued));

            var sw = Stopwatch.StartNew();
            var waiting = Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, 30, "wf-1");
            await Task.Delay(150);
            done = true;
            _notify.Should().NotBeNull("the wait subscribes before its first read");
            _notify!(RedisChannel.Literal("x"), "SUCCEEDED");

            var result = await waiting;
            sw.Stop();

            result.Status.Should().Be(FunctionQueueKeys.Wire.Succeeded);
            // Subscribed, the timer's first re-read is a full second out; the notification got
            // there well before it.
            sw.Elapsed.Should().BeLessThan(FunctionInvocationService.SubscribedPollInitial);
            _subscriber.Verify(s => s.UnsubscribeAsync(
                It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>?>(), It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task Without_pub_sub_the_wait_still_completes_by_polling()
        {
            _subscriber
                .Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketFailure, "no pubsub"));
            var sw = Stopwatch.StartNew();
            RunIs(() => sw.Elapsed > TimeSpan.FromMilliseconds(500) ? Run(RunStatus.Failed) : Run(RunStatus.Queued));

            var result = await Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, 5, "wf-1");

            result.Status.Should().Be(FunctionQueueKeys.Wire.Failed);
        }

        [Fact]
        public async Task Polling_backs_off_instead_of_reading_five_times_a_second()
        {
            _subscriber
                .Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketFailure, "no pubsub"));
            var reads = 0;
            _runs.Setup(r => r.GetByIdAsync(Tenant, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => Interlocked.Increment(ref reads))
                .ReturnsAsync(Run(RunStatus.Queued));

            await Service(("Functions:HttpSyncWaitMaxSeconds", "3"))
                .TestAsync(Tenant, "fn-1", new TestFunctionRequestDto { FunctionId = "fn-1", InputJson = "{}" });

            // A fixed 200 ms poll would read ~15 times in 3 s; 200→400→800→1000… reads ~6.
            reads.Should().BeInRange(3, 8);
        }

        [Fact]
        public async Task Cancelling_the_request_ends_the_wait_and_unsubscribes()
        {
            RunIs(() => Run(RunStatus.Queued));
            using var cts = new CancellationTokenSource();

            var waiting = Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, 30, "wf-1", cts.Token);
            await Task.Delay(100);
            var sw = Stopwatch.StartNew();
            await cts.CancelAsync();

            await FluentActions.Awaiting(() => waiting).Should().ThrowAsync<OperationCanceledException>();
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
            _subscriber.Verify(s => s.UnsubscribeAsync(
                It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>?>(), It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task A_run_the_runner_has_started_is_reported_running_when_the_wait_lapses()
        {
            RunIs(() => Run(RunStatus.Queued));
            _redis.Fake.On("HashGetAsync", _ => (RedisValue)FunctionQueueKeys.Wire.Running);

            var result = await Service(("Functions:HttpSyncWaitMaxSeconds", "1"))
                .TestAsync(Tenant, "fn-1", new TestFunctionRequestDto { FunctionId = "fn-1", InputJson = "{}", WaitTimeoutSeconds = 1 });

            result.Status.Should().Be(FunctionQueueKeys.Wire.Running);
        }
    }
}
