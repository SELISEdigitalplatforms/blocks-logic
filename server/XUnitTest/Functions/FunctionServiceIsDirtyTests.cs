using FluentAssertions;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Dirty detection compares the code and manifest hashes independently, matching
    /// <see cref="Services.FunctionBuildService"/>'s own build cache key, rather than a single
    /// combined hash the version does not otherwise store.
    /// </summary>
    public class FunctionServiceIsDirtyTests
    {
        private static FunctionEntity Function(string index = "a", string manifest = "{\"type\":\"module\"}") => new()
        {
            ItemId = "fn_1",
            Source = new FunctionSource { IndexJs = index, PackageJson = manifest },
        };

        [Fact]
        public void No_active_version_is_always_dirty()
        {
            FunctionService.IsDirty(Function(), null).Should().BeTrue();
        }

        [Fact]
        public void Matching_source_is_not_dirty()
        {
            var function = Function();
            var version = new FunctionVersionEntity
            {
                CodeHash = FunctionHashing.CodeHash(function.Source),
                ManifestHash = FunctionHashing.ManifestHash(function.Source),
            };

            FunctionService.IsDirty(function, version).Should().BeFalse();
        }

        [Fact]
        public void An_edited_entry_point_is_dirty()
        {
            var version = new FunctionVersionEntity
            {
                CodeHash = FunctionHashing.CodeHash(Function("original").Source),
                ManifestHash = FunctionHashing.ManifestHash(Function("original").Source),
            };

            FunctionService.IsDirty(Function("edited"), version).Should().BeTrue();
        }

        [Fact]
        public void An_edited_manifest_is_dirty_even_with_the_same_entry_point()
        {
            var version = new FunctionVersionEntity
            {
                CodeHash = FunctionHashing.CodeHash(Function(manifest: "{\"type\":\"module\"}").Source),
                ManifestHash = FunctionHashing.ManifestHash(Function(manifest: "{\"type\":\"module\"}").Source),
            };

            FunctionService.IsDirty(Function(manifest: "{\"type\":\"module\",\"dependencies\":{}}"), version)
                .Should().BeTrue();
        }

        [Fact]
        public void List_row_without_source_is_clean_when_the_stored_hash_matches_the_active_version()
        {
            var deployed = Function();
            // As the list reads it: Source projected away, SourceHash still on the document.
            var row = new FunctionEntity { ItemId = "fn_1", SourceHash = FunctionHashing.SourceHash(deployed.Source) };
            var version = new FunctionVersionEntity { Source = deployed.Source };

            FunctionService.IsDirtyByStoredHash(row, version).Should().BeFalse();
        }

        [Fact]
        public void List_row_is_dirty_when_the_stored_hash_differs_or_nothing_is_deployed()
        {
            var row = new FunctionEntity { ItemId = "fn_1", SourceHash = FunctionHashing.SourceHash(Function("edited").Source) };

            FunctionService.IsDirtyByStoredHash(row, new FunctionVersionEntity { Source = Function("original").Source })
                .Should().BeTrue();
            FunctionService.IsDirtyByStoredHash(row, null).Should().BeTrue();
        }
    }
}
