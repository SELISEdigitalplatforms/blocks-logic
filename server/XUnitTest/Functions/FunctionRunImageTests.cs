using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Utils;

namespace XUnitTest.Functions
{
    /// <summary>
    /// What a run of a deployed version is dispatched with. Registry builds run from their digest,
    /// unchanged; artifact-only builds (no digest) get a stable local name the runner builds the
    /// artifact under — an empty image made the runner dead-letter every such run.
    /// </summary>
    public class FunctionRunImageTests
    {
        [Fact]
        public void A_registry_build_runs_from_its_digest_even_when_it_also_has_an_artifact()
        {
            FunctionRunImage.For(new FunctionVersionEntity { ImageDigest = "reg/fn/x@sha256:abc", ArtifactId = "b1" })
                .Should().Be("reg/fn/x@sha256:abc");
        }

        [Fact]
        public void An_artifact_only_build_gets_a_lower_case_local_name_from_its_artifact_id()
        {
            FunctionRunImage.For(new FunctionVersionEntity { ImageDigest = "", ArtifactId = "E2CF9D6D-0724" })
                .Should().Be("blocks-fn-artifact/e2cf9d6d-0724:local");
        }

        [Fact]
        public void A_version_with_neither_has_no_image()
        {
            FunctionRunImage.For(new FunctionVersionEntity { ImageDigest = "", ArtifactId = null })
                .Should().BeEmpty();
        }
    }
}
