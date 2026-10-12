using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// A succeeded build is permanent and is what every later Test and Deploy of the same source
    /// reuses. That becomes a trap the moment its image is gone: each attempt is handed the same
    /// missing digest, fails to pull it, and nothing in the product displaces a cached success
    /// except a source change. This is the loop-breaker.
    /// </summary>
    public class FunctionImageRecoveryServiceTests
    {
        private const string Tenant = "t1";

        private readonly Mock<IFunctionBuildRepository> _builds = new(MockBehavior.Loose);

        private FunctionImageRecoveryService Service() =>
            new(_builds.Object, NullLogger<FunctionImageRecoveryService>.Instance);

        private static FunctionRunEntity Run(string? digest = "sha256:gone") =>
            new() { ItemId = "run_1", FunctionId = "fn_1", ImageDigest = digest };

        [Fact]
        public async Task A_pull_failure_invalidates_the_build_that_produced_the_image()
        {
            _builds.Setup(b => b.InvalidateByImageDigestAsync(
                    Tenant, "sha256:gone", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            var invalidated = await Service().HandleRunOutcomeAsync(Tenant, Run(), RunErrorCode.ImagePullFailed);

            invalidated.Should().Be(1);
            _builds.Verify(b => b.InvalidateByImageDigestAsync(
                Tenant, "sha256:gone", FunctionImageRecoveryService.InvalidationReason, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Theory]
        [InlineData(RunErrorCode.UserRuntimeError)]
        [InlineData(RunErrorCode.TimedOut)]
        [InlineData(RunErrorCode.MemoryLimit)]
        [InlineData(null)]
        public async Task Every_other_outcome_leaves_the_build_cache_alone(RunErrorCode? errorCode)
        {
            // A function that throws, or runs out of memory, has a perfectly good image. Rebuilding
            // on any failure would turn every broken handler into a rebuild loop.
            var invalidated = await Service().HandleRunOutcomeAsync(Tenant, Run(), errorCode);

            invalidated.Should().Be(0);
            _builds.Verify(b => b.InvalidateByImageDigestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task A_run_that_does_not_say_which_image_invalidates_nothing(string? digest)
        {
            // Runs recorded before the digest was kept. Guessing from the function's current source
            // could invalidate a build that is perfectly good.
            var invalidated = await Service().HandleRunOutcomeAsync(Tenant, Run(digest), RunErrorCode.ImagePullFailed);

            invalidated.Should().Be(0);
            _builds.Verify(b => b.InvalidateByImageDigestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task A_repository_failure_does_not_stop_the_result_being_applied()
        {
            // This runs inside the result consumer: throwing here would leave the run itself
            // unrecorded, which is worse than not recovering the build.
            _builds.Setup(b => b.InvalidateByImageDigestAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("mongo"));

            var invalidated = await Service().HandleRunOutcomeAsync(Tenant, Run(), RunErrorCode.ImagePullFailed);

            invalidated.Should().Be(0);
        }

        [Fact]
        public async Task Nothing_to_invalidate_is_a_quiet_zero()
        {
            _builds.Setup(b => b.InvalidateByImageDigestAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            (await Service().HandleRunOutcomeAsync(Tenant, Run(), RunErrorCode.ImagePullFailed)).Should().Be(0);
        }
    }
}
