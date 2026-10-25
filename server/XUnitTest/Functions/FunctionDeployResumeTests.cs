using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Functions
{
    /// <summary>
    /// A deploy that outlasts its request goes on as a build the editor watches. A page reload lost that build, so the
    /// editor showed Deploy again while it was still building. The editor now asks for the pending build on load, and a
    /// deploy of a build that is already the live version answers with that version instead of making a copy.
    /// </summary>
    public class FunctionDeployResumeTests
    {
        private const string Tenant = "t1";
        private const string FunctionId = "fn_1";
        private const string SourceHash = "hash_1";

        private readonly Mock<IFunctionRepository> _functions = new();
        private readonly Mock<IFunctionVersionRepository> _versions = new();
        private readonly Mock<IFunctionBuildRepository> _buildRepository = new();
        private readonly Mock<IFunctionBuildService> _builds = new();
        private readonly Mock<IValidator<DeployFunctionRequestDto>> _validator = new();

        private readonly FunctionEntity _function = new()
        {
            ItemId = FunctionId,
            SourceHash = SourceHash,
            Status = FunctionStatus.Live,
            ActiveVersionId = "v-2",
        };

        public FunctionDeployResumeTests()
        {
            _functions.Setup(f => f.GetByIdAsync(Tenant, FunctionId, It.IsAny<CancellationToken>())).ReturnsAsync(_function);
            _validator.Setup(v => v.ValidateAsync(It.IsAny<DeployFunctionRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
        }

        private static FunctionBuildEntity Build(BuildStatus status, DateTime? lastUpdated = null) => new()
        {
            ItemId = "b-1",
            FunctionId = FunctionId,
            SourceHash = SourceHash,
            Status = status,
            ArtifactSha256 = "abc",
            LastUpdatedDate = lastUpdated ?? DateTime.UtcNow,
        };

        private FunctionDeploymentService DeploymentService() => new(
            _functions.Object, _versions.Object, _builds.Object,
            new Mock<IFunctionVersionRetentionService>().Object,
            new Mock<IFunctionImagePinService>().Object,
            new Mock<IFunctionAuditService>().Object,
            _validator.Object, new Mock<ICacheClient>().Object,
            new ConfigurationBuilder().AddInMemoryCollection([]).Build(),
            NullLogger<FunctionDeploymentService>.Instance);

        private FunctionBuildService BuildService(FunctionBuildEntity? inProgress)
        {
            _buildRepository.Setup(b => b.GetInProgressBySourceHashAsync(Tenant, FunctionId, SourceHash, It.IsAny<CancellationToken>()))
                .ReturnsAsync(inProgress);
            return new FunctionBuildService(
                _buildRepository.Object,
                new Mock<global::Functions.DomainService.Storage.IFunctionArtifactStore>().Object,
                new Mock<ICacheClient>().Object,
                new ConfigurationBuilder().AddInMemoryCollection([]).Build(),
                NullLogger<FunctionBuildService>.Instance);
        }

        [Fact]
        public async Task Deploying_a_build_that_is_already_live_returns_that_version_and_creates_none()
        {
            _builds.Setup(b => b.GetAsync(Tenant, "b-1", It.IsAny<CancellationToken>())).ReturnsAsync(Build(BuildStatus.Succeeded));
            _versions.Setup(v => v.GetByIdAsync(Tenant, "v-2", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionVersionEntity { ItemId = "v-2", Number = 2, FunctionId = FunctionId, ArtifactId = "b-1" });

            var outcome = await DeploymentService().DeployAsync(
                Tenant, new DeployFunctionRequestDto { FunctionId = FunctionId, BuildId = "b-1" }, "u1", "u@x");

            outcome.Version!.Number.Should().Be(2);
            _versions.Verify(v => v.CreateAsync(Tenant, It.IsAny<FunctionVersionEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Deploying_a_build_that_is_not_live_yet_creates_a_version()
        {
            _builds.Setup(b => b.GetAsync(Tenant, "b-1", It.IsAny<CancellationToken>())).ReturnsAsync(Build(BuildStatus.Succeeded));
            _versions.Setup(v => v.GetByIdAsync(Tenant, "v-2", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionVersionEntity { ItemId = "v-2", Number = 2, FunctionId = FunctionId, ArtifactId = "b-0" });

            var outcome = await DeploymentService().DeployAsync(
                Tenant, new DeployFunctionRequestDto { FunctionId = FunctionId, BuildId = "b-1" }, "u1", "u@x");

            outcome.Version!.Number.Should().Be(1);
            _versions.Verify(v => v.CreateAsync(Tenant, It.IsAny<FunctionVersionEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_running_build_of_the_current_code_is_the_pending_deploy()
        {
            var pending = await BuildService(Build(BuildStatus.Building)).GetPendingAsync(Tenant, _function);

            pending!.ItemId.Should().Be("b-1");
        }

        [Fact]
        public async Task A_stale_build_is_not_pending_so_the_editor_offers_Deploy_again()
        {
            var stale = Build(BuildStatus.Queued, DateTime.UtcNow.AddMinutes(-35));

            var pending = await BuildService(stale).GetPendingAsync(Tenant, _function);

            pending.Should().BeNull();
        }
    }
}
