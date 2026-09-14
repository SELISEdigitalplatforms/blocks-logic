using Blocks.FunctionRunner.Maintenance;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Which references Image GC will delete from the registry.
    /// <para>
    /// Everything here is about not deleting the wrong thing. A reference is only accepted when it
    /// names this runner's own registry and pins a manifest by digest — a tag would name whatever
    /// the tag points at now, which after a rebuild is a live image.
    /// </para>
    /// </summary>
    public class RegistryClientTests
    {
        private const string Registry = "127.0.0.1:5000";

        [Fact]
        public void A_repo_digest_from_this_registry_is_split_into_name_and_digest()
        {
            var ok = RegistryClient.TryParse(
                "127.0.0.1:5000/fn/d238adf2-e71b-412b-b352-4d269b413797@sha256:7954c865a2c4",
                Registry, out var name, out var digest);

            ok.Should().BeTrue();
            name.Should().Be("fn/d238adf2-e71b-412b-b352-4d269b413797");
            digest.Should().Be("sha256:7954c865a2c4");
        }

        [Theory]
        // Another registry's image: not this process's to delete.
        [InlineData("docker.io/library/node@sha256:abc")]
        [InlineData("registry.example.com:5000/fn/abc@sha256:abc")]
        // A tag, not a digest — it would follow the tag to whatever is current.
        [InlineData("127.0.0.1:5000/fn/abc:568a5ad80f90-e33a6815d061")]
        [InlineData("127.0.0.1:5000/fn/abc")]
        // Malformed.
        [InlineData("127.0.0.1:5000/fn/abc@")]
        [InlineData("127.0.0.1:5000/fn/abc@md5:abc")]
        [InlineData("@sha256:abc")]
        [InlineData("")]
        [InlineData(null)]
        public void Anything_else_is_refused(string? reference)
        {
            RegistryClient.TryParse(reference, Registry, out _, out _).Should().BeFalse();
        }

        [Fact]
        public void A_traversing_repository_name_is_refused()
        {
            // The name goes straight into a URL path; '..' in it would address another repository.
            RegistryClient.TryParse("127.0.0.1:5000/fn/../../other@sha256:abc", Registry, out _, out _)
                .Should().BeFalse();
        }

        [Fact]
        public void The_base_image_repository_is_parsed_like_any_other()
        {
            // Parsing is not permission: Image GC decides what may be deleted, and it never offers
            // the base image. Keeping this honest means one rule, not two.
            RegistryClient.TryParse($"{Registry}/blocks/functions-node@sha256:abc", Registry, out var name, out _)
                .Should().BeTrue();
            name.Should().Be("blocks/functions-node");
        }
    }
}
