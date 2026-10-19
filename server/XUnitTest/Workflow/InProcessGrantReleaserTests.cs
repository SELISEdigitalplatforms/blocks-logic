using Blocks.Genesis;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Workflow.DomainService.Services;

namespace XUnitTest.Workflow
{
    public class InProcessGrantReleaserTests
    {
        private static readonly string GrantId = "dg_" + new string('b', 64);

        [Fact]
        public async Task Release_deletes_the_grant_and_drops_its_cached_token()
        {
            var store = new Mock<IDelegationGrantStore>();
            var tokens = new Mock<IDelegatedTokenProvider>();
            var sut = new InProcessGrantReleaser(store.Object, tokens.Object, NullLogger<InProcessGrantReleaser>.Instance);

            await sut.ReleaseAsync(GrantId);

            tokens.Verify(t => t.Invalidate(GrantId), Times.Once);
            store.Verify(s => s.DeleteAsync(GrantId), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public async Task Release_does_nothing_without_a_grant(string? grantId)
        {
            var store = new Mock<IDelegationGrantStore>();
            var tokens = new Mock<IDelegatedTokenProvider>();
            var sut = new InProcessGrantReleaser(store.Object, tokens.Object, NullLogger<InProcessGrantReleaser>.Instance);

            await sut.ReleaseAsync(grantId);

            store.VerifyNoOtherCalls();
            tokens.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Release_never_throws_when_the_store_fails()
        {
            var store = new Mock<IDelegationGrantStore>();
            store.Setup(s => s.DeleteAsync(It.IsAny<string>())).ThrowsAsync(new TimeoutException());
            var sut = new InProcessGrantReleaser(store.Object, Mock.Of<IDelegatedTokenProvider>(), NullLogger<InProcessGrantReleaser>.Instance);

            await sut.ReleaseAsync(GrantId);
        }
    }
}
