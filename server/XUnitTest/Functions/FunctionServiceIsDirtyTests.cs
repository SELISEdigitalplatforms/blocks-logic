using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Models;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;

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
    }
}
