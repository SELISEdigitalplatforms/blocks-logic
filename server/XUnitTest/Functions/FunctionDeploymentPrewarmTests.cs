using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Deploy's pre-warm request (sandbox/REUSE.md, "Pre-warm"): one entry on
    /// <c>functions:warm</c> when a version that opted into reuse goes Live, naming the version it
    /// replaced for draining — and never a reason for a deploy to fail.
    /// </summary>
    public class FunctionDeploymentPrewarmTests
    {
        private const string Tenant = "tenant-1";

        private readonly Mock<IFunctionRepository> _functions = new();
        private readonly Mock<IFunctionVersionRepository> _versions = new();
        private readonly Mock<IFunctionBuildService> _builds = new();
        private readonly Mock<IValidator<DeployFunctionRequestDto>> _validator = new();
        private readonly (IDatabase Database, FakeRedisDatabase Fake) _redis = FakeRedisDatabase.Create();
        private FunctionVersionEntity? _created;

        private readonly FunctionEntity _function = new()
        {
            ItemId = "fn-1",
            Status = FunctionStatus.Live,
            ActiveVersionId = "v-old",
        };

        public FunctionDeploymentPrewarmTests()
        {
            _functions.Setup(f => f.GetByIdAsync(Tenant, "fn-1", It.IsAny<CancellationToken>())).ReturnsAsync(_function);
            _builds.Setup(b => b.EnsureImageAsync(Tenant, It.IsAny<FunctionEntity>(), It.IsAny<CancellationToken>(), It.IsAny<int?>(), It.IsAny<bool>()))
                .ReturnsAsync(new FunctionBuildEntity { ItemId = "b-1", Status = BuildStatus.Succeeded, ImageDigest = "registry/fn@sha256:new" });
            _versions.Setup(v => v.CreateAsync(Tenant, It.IsAny<FunctionVersionEntity>(), It.IsAny<CancellationToken>()))
                .Callback((string _, FunctionVersionEntity v, CancellationToken _) => _created = v)
                .Returns(Task.CompletedTask);
            _validator.Setup(v => v.ValidateAsync(It.IsAny<DeployFunctionRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
        }

        private global::Functions.DomainService.Storage.IFunctionArtifactStore? _artifactStore;

        private FunctionDeploymentService Service(params (string Key, string Value)[] settings)
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_redis.Database);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
                .Build();

            return new FunctionDeploymentService(
                _functions.Object, _versions.Object, _builds.Object,
                new Mock<IFunctionVersionRetentionService>().Object,
                new Mock<IFunctionImagePinService>().Object,
                new Mock<IFunctionAuditService>().Object,
                _validator.Object, cache.Object, configuration,
                NullLogger<FunctionDeploymentService>.Instance, _artifactStore);
        }

        [Fact]
        public async Task A_prewarm_of_an_artifact_built_version_carries_the_artifact_url_and_hash()
        {
            _function.Trigger.ReuseSandbox = true;
            _builds.Setup(b => b.EnsureImageAsync(Tenant, It.IsAny<FunctionEntity>(), It.IsAny<CancellationToken>(), It.IsAny<int?>(), It.IsAny<bool>()))
                .ReturnsAsync(new FunctionBuildEntity { ItemId = "b-art", Status = BuildStatus.Succeeded, ArtifactSha256 = "abc123" });
            var store = new Mock<global::Functions.DomainService.Storage.IFunctionArtifactStore>();
            store.Setup(s => s.CreateDownloadUrlAsync(Tenant, "b-art", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("https://blob/b-art.tar?sig=1");
            _artifactStore = store.Object;

            await DeployAsync(Service());

            var entry = WarmEntries().Should().ContainSingle().Subject;
            Field(entry, "image").Should().Be("blocks-fn-artifact/b-art:local");
            Field(entry, FunctionQueueKeys.RunArtifactUrlField).Should().Be("https://blob/b-art.tar?sig=1");
            Field(entry, FunctionQueueKeys.RunArtifactSha256Field).Should().Be("abc123");
        }

        private Task<global::Functions.DomainService.Dtos.Responses.FunctionVersionSummaryDto> DeployAsync(FunctionDeploymentService service) =>
            service.DeployAsync(Tenant, new DeployFunctionRequestDto { FunctionId = "fn-1" }, "user-1", "u@x");

        private IReadOnlyList<object?[]> WarmEntries() =>
            _redis.Fake.Calls("StreamAddAsync").Where(c => ((RedisKey)c[0]!).ToString() == FunctionQueueKeys.WarmStream).ToList();

        private static string Field(object?[] call, string name) =>
            ((NameValueEntry[])call[1]!).Single(e => e.Name == name).Value.ToString();

        [Fact]
        public async Task A_version_that_opted_in_is_prewarmed_and_the_one_it_replaced_drained()
        {
            _function.Trigger.ReuseSandbox = true;

            await DeployAsync(Service());

            var entry = WarmEntries().Should().ContainSingle().Subject;
            Field(entry, "tenantId").Should().Be(Tenant);
            Field(entry, "functionId").Should().Be("fn-1");
            Field(entry, "versionId").Should().Be(_created!.ItemId);
            Field(entry, "image").Should().Be("registry/fn@sha256:new");
            Field(entry, "count").Should().Be("1");
            Field(entry, "drainVersionId").Should().Be("v-old");
        }

        [Fact]
        public async Task The_count_comes_from_configuration()
        {
            _function.Trigger.ReuseSandbox = true;

            await DeployAsync(Service(("Functions:PrewarmCount", "3")));

            Field(WarmEntries().Single(), "count").Should().Be("3");
        }

        [Fact]
        public async Task A_first_deploy_has_nothing_to_drain()
        {
            _function.Trigger.ReuseSandbox = true;
            _function.ActiveVersionId = null;
            _function.Status = FunctionStatus.Draft;

            await DeployAsync(Service());

            Field(WarmEntries().Single(), "drainVersionId").Should().BeEmpty();
        }

        [Fact]
        public async Task Every_deploy_prewarms_even_without_the_old_per_function_switch()
        {
            // Reuse is always on (2026-10-06): the stored ReuseSandbox flag is ignored.
            _function.Trigger.ReuseSandbox = false;

            await DeployAsync(Service());

            var entry = WarmEntries().Should().ContainSingle().Subject;
            Field(entry, "count").Should().Be("1");
            Field(entry, "drainVersionId").Should().Be("v-old");
        }

        [Fact]
        public async Task A_prewarm_count_of_zero_starts_nothing_but_still_drains()
        {
            _function.Trigger.ReuseSandbox = true;

            await DeployAsync(Service(("Functions:PrewarmCount", "0")));

            var entry = WarmEntries().Single();
            Field(entry, "count").Should().Be("0");
            Field(entry, "drainVersionId").Should().Be("v-old");
        }

        [Fact]
        public async Task The_warm_stream_is_trimmed_on_every_write()
        {
            _function.Trigger.ReuseSandbox = true;

            await DeployAsync(Service());

            // StreamAddAsync(key, fields, messageId, maxLength, useApproximateMaxLength, …)
            var call = WarmEntries().Single();
            call.Should().Contain(1000L).And.Contain(true);
        }

        [Fact]
        public async Task A_publish_failure_never_fails_the_deploy()
        {
            _function.Trigger.ReuseSandbox = true;
            _redis.Fake.On("StreamAddAsync", _ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));

            var result = await DeployAsync(Service());

            result.Should().NotBeNull();
            _functions.Verify(f => f.SetActiveVersionAsync(Tenant, "fn-1", _created!.ItemId, _created.Number,
                It.IsAny<DateTime>(), "user-1", It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
