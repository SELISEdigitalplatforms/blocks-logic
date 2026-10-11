using System.Formats.Tar;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Sandbox;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Unpacking a run's artifact into a build context. It used to shell out to the host's `tar`,
    /// which under the runner unit's RestrictSUIDSGID=true could not create any file below the top
    /// level (openat2 → ENOSYS), so every artifact run failed. These pin the in-process path: the
    /// real artifact layout (top-level files plus manifest/ and src/) comes out whole, and an entry
    /// that points outside the context is refused.
    /// </summary>
    public sealed class ArtifactExtractionTests : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("fn-artifact-extract-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        private static ArtifactImageBuilder Builder() =>
            new(new NoHttp(), Microsoft.Extensions.Options.Options.Create(new RunnerOptions()),
                NullLogger<ArtifactImageBuilder>.Instance);

        private sealed class NoHttp : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => throw new InvalidOperationException("not used");
        }

        [Fact]
        public async Task The_artifact_layout_with_subdirectories_is_extracted_whole()
        {
            var source = Path.Combine(_root, "source");
            Directory.CreateDirectory(Path.Combine(source, "manifest"));
            Directory.CreateDirectory(Path.Combine(source, "src"));
            await File.WriteAllTextAsync(Path.Combine(source, "Dockerfile"), "FROM scratch");
            await File.WriteAllTextAsync(Path.Combine(source, "deps.tar"), "deps");
            await File.WriteAllTextAsync(Path.Combine(source, "manifest", "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(source, "src", "index.js"), "export default () => 1;");
            var archive = Path.Combine(_root, "context.tar");
            await TarFile.CreateFromDirectoryAsync(source, archive, includeBaseDirectory: false);

            var context = Path.Combine(_root, "context");
            Directory.CreateDirectory(context);

            (await Builder().ExtractAsync(archive, context, CancellationToken.None)).Should().BeTrue();
            (await File.ReadAllTextAsync(Path.Combine(context, "manifest", "package.json"))).Should().Be("{}");
            (await File.ReadAllTextAsync(Path.Combine(context, "src", "index.js"))).Should().Be("export default () => 1;");
            File.Exists(Path.Combine(context, "Dockerfile")).Should().BeTrue();
            File.Exists(Path.Combine(context, "deps.tar")).Should().BeTrue();
        }

        [Fact]
        public async Task An_entry_that_would_land_outside_the_context_is_refused()
        {
            var archive = Path.Combine(_root, "evil.tar");
            await using (var stream = File.Create(archive))
            await using (var writer = new TarWriter(stream))
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, "../escaped.txt")
                {
                    DataStream = new MemoryStream("x"u8.ToArray()),
                };
                await writer.WriteEntryAsync(entry);
            }

            var context = Path.Combine(_root, "context");
            Directory.CreateDirectory(context);

            (await Builder().ExtractAsync(archive, context, CancellationToken.None)).Should().BeFalse();
            File.Exists(Path.Combine(_root, "escaped.txt")).Should().BeFalse();
        }
    }
}
