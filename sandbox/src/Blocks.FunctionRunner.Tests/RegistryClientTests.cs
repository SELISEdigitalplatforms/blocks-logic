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

        /// <summary>
        /// The check a build now makes before publishing a digest, against a real registry. It is
        /// the question a later pull asks, and asking the daemon instead — which is what the build
        /// used to do — answers about the local image store, where a never-pushed image looks
        /// exactly like a pushed one.
        /// </summary>
        [SkippableFact]
        public async Task A_digest_the_registry_does_not_hold_is_reported_absent()
        {
            // A separate client for the probe: RegistryClient sets Timeout in its constructor,
            // and HttpClient refuses that once the instance has sent a request.
            using (var probe = new HttpClient())
            {
                Skip.If(!await RegistryReachableAsync(probe), "no local registry");
            }

            using var http = new HttpClient();
            var client = new RegistryClient(
                http,
                Microsoft.Extensions.Options.Options.Create(
                    new Blocks.FunctionRunner.Options.RunnerOptions { Registry = Registry }),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RegistryClient>.Instance);

            var absent = await client.ManifestExistsAsync(
                $"{Registry}/fn/no-such-repository@sha256:{new string('d', 64)}");

            absent.Should().BeFalse();
        }

        /// <summary>
        /// The positive half, and the one that matters most: a build now refuses to publish a
        /// digest this call cannot confirm, so a manifest the registry <i>does</i> hold must come
        /// back true. It only does with the right Accept headers — a registry answers 404 for a
        /// manifest in a media type the request did not ask for, and that would fail every build.
        /// </summary>
        [SkippableFact]
        public async Task A_digest_the_registry_does_hold_is_confirmed()
        {
            string? repoDigest;
            using (var probe = new HttpClient())
            {
                repoDigest = await AnyStoredRepoDigestAsync(probe);
            }

            Skip.If(repoDigest is null, "no local registry, or it holds no tagged image");

            using var http = new HttpClient();
            var client = new RegistryClient(
                http,
                Microsoft.Extensions.Options.Options.Create(
                    new Blocks.FunctionRunner.Options.RunnerOptions { Registry = Registry }),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RegistryClient>.Instance);

            (await client.ManifestExistsAsync(repoDigest!)).Should().BeTrue();
        }

        /// <summary>Any repo@digest the local registry is actually serving, or null.</summary>
        private static async Task<string?> AnyStoredRepoDigestAsync(HttpClient http)
        {
            try
            {
                using var catalog = await http.GetAsync(new Uri($"http://{Registry}/v2/_catalog"));
                if (!catalog.IsSuccessStatusCode) return null;

                using var catalogJson = System.Text.Json.JsonDocument.Parse(
                    await catalog.Content.ReadAsStringAsync());

                foreach (var repository in catalogJson.RootElement.GetProperty("repositories").EnumerateArray())
                {
                    var name = repository.GetString();
                    if (name is null) continue;

                    using var tags = await http.GetAsync(new Uri($"http://{Registry}/v2/{name}/tags/list"));
                    if (!tags.IsSuccessStatusCode) continue;

                    using var tagsJson = System.Text.Json.JsonDocument.Parse(
                        await tags.Content.ReadAsStringAsync());
                    if (tagsJson.RootElement.GetProperty("tags").ValueKind != System.Text.Json.JsonValueKind.Array)
                        continue;

                    foreach (var tag in tagsJson.RootElement.GetProperty("tags").EnumerateArray())
                    {
                        using var request = new HttpRequestMessage(
                            HttpMethod.Head, new Uri($"http://{Registry}/v2/{name}/manifests/{tag.GetString()}"));
                        foreach (var mediaType in new[]
                                 {
                                     "application/vnd.oci.image.index.v1+json",
                                     "application/vnd.oci.image.manifest.v1+json",
                                     "application/vnd.docker.distribution.manifest.list.v2+json",
                                     "application/vnd.docker.distribution.manifest.v2+json",
                                 })
                        {
                            request.Headers.Accept.Add(
                                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(mediaType));
                        }

                        using var head = await http.SendAsync(request);
                        if (!head.IsSuccessStatusCode) continue;
                        if (!head.Headers.TryGetValues("Docker-Content-Digest", out var values)) continue;

                        return $"{Registry}/{name}@{values.First()}";
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
            {
                return null;
            }

            return null;
        }

        [Fact]
        public async Task A_reference_from_another_registry_is_inconclusive_rather_than_absent()
        {
            // Null, not false: this process cannot answer for a registry that is not its own, and
            // a build must not be failed on a question that was never asked.
            using var http = new HttpClient();
            var client = new RegistryClient(
                http,
                Microsoft.Extensions.Options.Options.Create(
                    new Blocks.FunctionRunner.Options.RunnerOptions { Registry = Registry }),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RegistryClient>.Instance);

            (await client.ManifestExistsAsync($"registry.example.com/fn/x@sha256:{new string('d', 64)}"))
                .Should().BeNull();
        }

        private static async Task<bool> RegistryReachableAsync(HttpClient http)
        {
            try
            {
                using var response = await http.GetAsync(new Uri($"http://{Registry}/v2/"));
                return true;
            }
            catch (HttpRequestException)
            {
                return false;
            }
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
