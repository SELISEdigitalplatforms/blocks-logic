using FluentAssertions;
using Functions.DomainService.Storage;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The artifact holds a tenant's source and its whole dependency tree, so where it lands is a
    /// security property, not a naming convention. These cover the part that decides that — the
    /// path — without needing a storage account.
    /// </summary>
    public class FunctionArtifactStoreTests
    {
        [Fact]
        public void An_artifact_is_addressed_by_tenant_and_build_id()
        {
            FunctionArtifactStore.BlobPath("tenant-1", "abc123").Should().Be("tenant-1/abc123.tar");
        }

        /// <summary>
        /// Build ids are never reused, so a path is never reused for different content — which is
        /// what makes an upload safe while another host may still be reading the previous one.
        /// </summary>
        [Fact]
        public void Different_content_never_shares_a_path()
        {
            FunctionArtifactStore.BlobPath("tenant-1", "aaa")
                .Should().NotBe(FunctionArtifactStore.BlobPath("tenant-1", "bbb"));
        }

        [Fact]
        public void One_tenant_can_never_be_addressed_from_anothers_prefix()
        {
            FunctionArtifactStore.BlobPath("tenant-1", "abc")
                .Should().StartWith("tenant-1/");
            FunctionArtifactStore.BlobPath("tenant-2", "abc")
                .Should().StartWith("tenant-2/");
        }

        /// <summary>
        /// Azure container and blob names are case-sensitive, and a tenant id that reached here in
        /// two casings would be two tenants as far as the store is concerned.
        /// </summary>
        [Fact]
        public void Tenant_casing_does_not_split_one_tenant_into_two()
        {
            FunctionArtifactStore.BlobPath("Tenant-1", "abc")
                .Should().Be(FunctionArtifactStore.BlobPath("tenant-1", "abc"));
        }

        /// <summary>
        /// Both halves reach a URL. A separator in either would let a path climb out of the tenant's
        /// prefix, which is the only thing keeping one tenant's artifact away from another.
        /// </summary>
        [Theory]
        [InlineData("../other-tenant")]
        [InlineData("tenant/../other")]
        [InlineData("tenant\\other")]
        [InlineData("a/b")]
        public void A_tenant_id_that_could_climb_out_is_refused(string tenantId)
        {
            var act = () => FunctionArtifactStore.BlobPath(tenantId, "abc");

            act.Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData("../abc")]
        [InlineData("a/b")]
        [InlineData("a\\b")]
        public void An_artifact_id_that_could_climb_out_is_refused(string hash)
        {
            var act = () => FunctionArtifactStore.BlobPath("tenant-1", hash);

            act.Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void A_missing_tenant_is_refused_rather_than_defaulted(string? tenantId)
        {
            // There is no sensible fallback. Writing to a blank prefix would put one tenant's source
            // at the root of a container every other tenant's SAS could be issued against.
            var act = () => FunctionArtifactStore.BlobPath(tenantId!, "abc");

            act.Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void A_missing_artifact_id_is_refused(string? hash)
        {
            var act = () => FunctionArtifactStore.BlobPath("tenant-1", hash!);

            act.Should().Throw<ArgumentException>();
        }

        /// <summary>
        /// Pinned so the container cannot quietly become the tenant content container, which is
        /// created with public blob access and would make every artifact anonymously readable.
        /// </summary>
        [Fact]
        public void Artifacts_live_in_their_own_container()
        {
            FunctionArtifactStore.ContainerName.Should().Be("blocks-fn-artifacts");
        }
    }
}
