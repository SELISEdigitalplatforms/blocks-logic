using FluentAssertions;
using Functions.DomainService.Models;
using Functions.DomainService.Utils;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The tag half of the image reference a build is pushed under.
    /// <para>
    /// Docker's grammar caps a tag at 128 characters. Both hashes are full sha256 hex — 64 each —
    /// so <c>{codeHash}-{manifestHash}</c> came to 129 and the Engine rejected every single build
    /// with <c>invalid reference format</c> before it started. One character over, and no function
    /// on the platform could ever build. These pin the ceiling and the identity.
    /// </para>
    /// </summary>
    public class BuildImageTagTests
    {
        /// <summary>Docker: <c>[\w][\w.-]{0,127}</c>.</summary>
        private const int DockerTagLimit = 128;

        private const int TagHashChars = 12;

        private static FunctionSource Source(string indexJs, string packageJson = "{}") =>
            new() { IndexJs = indexJs, PackageJson = packageJson };

        /// <summary>Mirrors FunctionBuildService.QueueBuildAsync's tag construction.</summary>
        private static string Tag(FunctionSource source)
        {
            var code = FunctionHashing.CodeHash(source);
            var manifest = FunctionHashing.ManifestHash(source);
            return $"{code[..Math.Min(TagHashChars, code.Length)]}-" +
                   $"{manifest[..Math.Min(TagHashChars, manifest.Length)]}";
        }

        [Fact]
        public void FullHashes_WouldHaveExceededDockersLimit()
        {
            var source = Source("export default async () => 1;");

            var untruncated =
                $"{FunctionHashing.CodeHash(source)}-{FunctionHashing.ManifestHash(source)}";

            // The regression this guards against: 129 > 128.
            untruncated.Length.Should().BeGreaterThan(DockerTagLimit);
        }

        [Fact]
        public void Tag_FitsWellInsideTheLimit()
        {
            Tag(Source("export default async () => 1;")).Length
                .Should().BeLessThanOrEqualTo(DockerTagLimit);
        }

        [Fact]
        public void Tag_IsAValidDockerTag()
        {
            var tag = Tag(Source("export default async () => 1;"));

            tag.Should().MatchRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$");
        }

        [Fact]
        public void Tag_ChangesWithTheCode()
        {
            Tag(Source("export default async () => 1;"))
                .Should().NotBe(Tag(Source("export default async () => 2;")));
        }

        [Fact]
        public void Tag_ChangesWithTheDependencies()
        {
            var code = "export default async () => 1;";

            Tag(Source(code, "{\"dependencies\":{\"zod\":\"3.23.8\"}}"))
                .Should().NotBe(Tag(Source(code, "{\"dependencies\":{\"zod\":\"3.24.0\"}}")));
        }

        [Fact]
        public void Tag_IsStableForTheSameSource()
        {
            var code = "export default async () => 1;";

            Tag(Source(code)).Should().Be(Tag(Source(code)));
        }
    }
}
