using FluentAssertions;
using Functions.DomainService.Models;
using Functions.DomainService.Utils;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Hashing decides two user-visible things: whether the editor shows unsaved changes, and
    /// whether a build is reused. Getting it too sensitive means pointless rebuilds and a
    /// "dirty" badge that never clears; too insensitive means deploying code that was never
    /// built. Both fail quietly, which is why these are pinned.
    /// </summary>
    public class FunctionHashingTests
    {
        private static FunctionSource Source(string index = "export default async () => 1;",
                                             string manifest = "{\"type\":\"module\"}",
                                             string? lockJson = null)
            => new() { IndexJs = index, PackageJson = manifest, LockJson = lockJson };

        [Fact]
        public void Identical_source_hashes_identically()
        {
            FunctionHashing.SourceHash(Source()).Should().Be(FunctionHashing.SourceHash(Source()));
        }

        [Fact]
        public void A_changed_entry_point_changes_the_hash()
        {
            FunctionHashing.SourceHash(Source(index: "export default async () => 2;"))
                .Should().NotBe(FunctionHashing.SourceHash(Source()));
        }

        [Fact]
        public void A_changed_manifest_changes_the_hash()
        {
            FunctionHashing.SourceHash(Source(manifest: "{\"type\":\"module\",\"dependencies\":{}}"))
                .Should().NotBe(FunctionHashing.SourceHash(Source()));
        }

        [Fact]
        public void A_changed_lockfile_changes_the_hash()
        {
            FunctionHashing.SourceHash(Source(lockJson: "{\"lockfileVersion\":3}"))
                .Should().NotBe(FunctionHashing.SourceHash(Source()));
        }

        [Fact]
        public void Line_endings_do_not_change_the_hash()
        {
            // The same file saved on Windows and on Linux must reuse the same build; the
            // runtime cannot tell them apart, so neither should we.
            var unix = Source(index: "line one\nline two\n");
            var windows = Source(index: "line one\r\nline two\r\n");

            FunctionHashing.SourceHash(windows).Should().Be(FunctionHashing.SourceHash(unix));
        }

        [Fact]
        public void Trailing_whitespace_and_a_final_newline_do_not_change_the_hash()
        {
            var plain = Source(index: "const a = 1;\nconst b = 2;");
            var untidy = Source(index: "const a = 1;   \nconst b = 2;\t\n\n");

            FunctionHashing.SourceHash(untidy).Should().Be(FunctionHashing.SourceHash(plain));
        }

        [Fact]
        public void Indentation_is_content_and_does_change_the_hash()
        {
            // Leading whitespace can matter inside template literals, so it is not normalised.
            FunctionHashing.SourceHash(Source(index: "  const a = 1;"))
                .Should().NotBe(FunctionHashing.SourceHash(Source(index: "const a = 1;")));
        }

        [Fact]
        public void Blank_lines_inside_the_file_are_content()
        {
            FunctionHashing.SourceHash(Source(index: "a\n\nb"))
                .Should().NotBe(FunctionHashing.SourceHash(Source(index: "a\nb")));
        }

        [Fact]
        public void Field_boundaries_cannot_be_confused()
        {
            // Without length-prefixing, ("ab","c") and ("a","bc") would hash the same and a
            // source change could masquerade as no change at all.
            FunctionHashing.SourceHash(Source(index: "ab", manifest: "c"))
                .Should().NotBe(FunctionHashing.SourceHash(Source(index: "a", manifest: "bc")));
        }

        [Fact]
        public void Manifest_hash_ignores_the_entry_point()
        {
            // A source-only change must reuse the dependency layer (DECISIONS D3).
            var before = Source(index: "export default async () => 1;");
            var after = Source(index: "export default async () => 999;");

            FunctionHashing.ManifestHash(after).Should().Be(FunctionHashing.ManifestHash(before));
            FunctionHashing.CodeHash(after).Should().NotBe(FunctionHashing.CodeHash(before));
        }

        [Fact]
        public void Manifest_hash_tracks_the_lockfile()
        {
            FunctionHashing.ManifestHash(Source(lockJson: "{\"a\":1}"))
                .Should().NotBe(FunctionHashing.ManifestHash(Source(lockJson: "{\"a\":2}")));
        }

        [Fact]
        public void A_function_with_nothing_deployed_is_dirty()
        {
            FunctionHashing.IsDirty(Source(), null).Should().BeTrue();
            FunctionHashing.IsDirty(Source(), string.Empty).Should().BeTrue();
        }

        [Fact]
        public void A_function_matching_its_deployed_version_is_clean()
        {
            var source = Source();
            FunctionHashing.IsDirty(source, FunctionHashing.SourceHash(source)).Should().BeFalse();
        }

        [Fact]
        public void An_edited_function_is_dirty_again()
        {
            var deployed = FunctionHashing.SourceHash(Source());
            FunctionHashing.IsDirty(Source(index: "export default async () => 3;"), deployed)
                .Should().BeTrue();
        }

        [Fact]
        public void Hashes_are_lowercase_hex_of_the_expected_length()
        {
            var hash = FunctionHashing.SourceHash(Source());
            hash.Should().MatchRegex("^[0-9a-f]{64}$");
        }
    }
}
