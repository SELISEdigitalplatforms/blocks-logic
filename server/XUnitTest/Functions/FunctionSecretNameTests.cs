using System.Net;
using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using FluentValidation;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Functions.DomainService.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// A secret kept by NAME (approved 2026-10-18): an import stores <c>{{secretName.X}}</c> when the target has no single
    /// secret called X. Save accepts it, the envelope masks it, output actions look it up exactly at send, and the
    /// "needs secret" warning is computed on read so it clears itself once the secret exists. Plus the tenant in new
    /// function image names, with the old name still handled.
    /// </summary>
    public class FunctionSecretNameTests
    {
        private const string Tenant = "tenant_1";

        // ======================================================= save / literal check ====

        [Theory]
        [InlineData("{{secretName.stripe-key}}")]
        [InlineData("Bearer {{secretName.api.token_2}}")]
        [InlineData("{{secretUnresolved}}")]
        public void A_name_reference_is_a_reference_not_a_typed_credential(string value)
        {
            FunctionSecretLiterals.HasLiteral(value).Should().BeFalse();
            FunctionSecretLiterals.FindNewLiterals(
                [new VariableBinding { Key = "API_KEY", Value = value }], null, null, null).Should().BeEmpty();
        }

        [Theory]
        [InlineData("{{secretName.}}")]
        [InlineData("{{secretName.-bad}}")]
        [InlineData("{{secretName.stripe-key}} sk_live_123")]
        public void Anything_else_under_a_credential_name_is_still_refused(string value)
        {
            FunctionSecretLiterals.HasLiteral(value).Should().BeTrue();
        }

        [Fact]
        public async Task Save_accepts_a_name_reference()
        {
            var functions = new Mock<IFunctionRepository>();
            functions.Setup(f => f.GetByIdAsync(Tenant, "fn_1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionEntity { ItemId = "fn_1", Name = "n" });
            var service = new FunctionService(
                functions.Object, Mock.Of<IFunctionVersionRepository>(), Mock.Of<IFunctionRunRepository>(),
                Mock.Of<IFunctionAuditService>(), Mock.Of<IFunctionDeletionQueue>(), Mock.Of<IFunctionUsageService>(),
                new CreateFunctionRequestValidator(), new UpdateFunctionRequestValidator(), new SaveFunctionRequestValidator(),
                NullLogger<FunctionService>.Instance, new ConfigurationBuilder().Build());

            var saved = await service.SaveAsync(Tenant, new SaveFunctionRequestDto
            {
                FunctionId = "fn_1",
                IndexJs = "export default async () => 1;",
                PackageJson = """{"type":"module"}""",
                Variables = [new VariableBinding { Key = "STRIPE_API_KEY", Value = "{{secretName.stripe-key}}" }],
                OutputActions =
                [
                    new OutputAction
                    {
                        Url = "https://hooks.example.com/x",
                        Headers = new() { ["Authorization"] = "Bearer {{secretName.stripe-key}}" },
                    },
                ],
            }, "u", "e");

            saved.Variables.Single().Value.Should().Be("{{secretName.stripe-key}}");
        }

        // ============================================================== envelope ====

        [Fact]
        public void The_envelope_keeps_the_name_reference_and_marks_the_key_for_masking()
        {
            var function = new FunctionEntity
            {
                ItemId = "fn_1",
                Variables = [new VariableBinding { Key = "STRIPE_KEY", Value = "{{secretName.stripe-key}}" }],
            };

            var json = FunctionEnvelopeBuilder.Build(new FunctionRunEntity { ItemId = "r", FunctionId = "fn_1" }, null, function, null, null);

            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("env").GetProperty("STRIPE_KEY").GetString().Should().Be("{{secretName.stripe-key}}");
            doc.RootElement.GetProperty("maskedEnv").EnumerateArray().Select(e => e.GetString()).Should().Contain("STRIPE_KEY");
        }

        // ======================================================== output actions ====

        private sealed class NamedResolver(Dictionary<string, string> values, Dictionary<string, string[]> idsByName) : ISecretResolver
        {
            public Dictionary<string, string[]> IdsByName { get; } = idsByName;
            public List<string[]> ValueRequests { get; } = [];
            public List<string[]> NameRequests { get; } = [];

            public Task<IReadOnlyDictionary<string, string>> ResolveAsync(
                IReadOnlyCollection<string> secretIds, string tenantId, CancellationToken cancellationToken = default)
            {
                ValueRequests.Add(secretIds.ToArray());
                return Task.FromResult<IReadOnlyDictionary<string, string>>(
                    secretIds.Where(values.ContainsKey).ToDictionary(id => id, id => values[id]));
            }

            public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> FindIdsByNameAsync(
                IReadOnlyCollection<string> names, string tenantId, CancellationToken cancellationToken = default)
            {
                NameRequests.Add(names.ToArray());
                return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(names.ToDictionary(
                    n => n, n => (IReadOnlyList<string>)(IdsByName.TryGetValue(n, out var ids) ? ids : [])));
            }
        }

        private sealed class Capture(List<HttpRequestMessage> seen) : HttpMessageHandler
        {
            public static readonly Dictionary<HttpRequestMessage, string?> Bodies = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                seen.Add(request);
                var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                lock (Bodies) Bodies[request] = body;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        }

        private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
        }

        private static OutputActionProcessor Processor(ISecretResolver resolver, List<HttpRequestMessage> seen) => new(
            resolver, new Factory(new Capture(seen)),
            new FunctionOutboundGuard((_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") })),
            NullLogger<OutputActionProcessor>.Instance);

        private static OutputAction Action(string header) => new()
        {
            Id = "a1",
            Url = "https://hooks.example.com/x",
            Headers = new() { ["Authorization"] = header },
            TimeoutSeconds = 5,
        };

        [Fact]
        public async Task An_output_action_name_with_one_match_sends_the_value_read_in_one_batch()
        {
            var resolver = new NamedResolver(new() { ["s1"] = "v_named", ["s2"] = "v_id" }, new() { ["stripe-key"] = ["s1"] });
            var seen = new List<HttpRequestMessage>();
            var action = Action("Bearer {{secretName.stripe-key}} {{secret.s2}}");

            var chain = await Processor(resolver, seen).ProcessAsync(Tenant, new FunctionRunEntity { ItemId = "r" }, [action], new RetryPolicy { Attempts = 1 });

            chain.AllSucceeded.Should().BeTrue();
            seen.Single().Headers.GetValues("Authorization").Single().Should().Be("Bearer v_named v_id");
            resolver.ValueRequests.Should().ContainSingle().Which.Should().BeEquivalentTo(["s1", "s2"]);
        }

        [Fact]
        public async Task An_output_action_name_with_no_secret_fails_clearly_and_sends_nothing()
        {
            var resolver = new NamedResolver(new(), new());
            var seen = new List<HttpRequestMessage>();

            var chain = await Processor(resolver, seen).ProcessAsync(Tenant, new FunctionRunEntity { ItemId = "r" },
                [Action("{{secretName.stripe-key}}")], new RetryPolicy { Attempts = 1 });

            chain.AllSucceeded.Should().BeFalse();
            chain.Results.Single().Error.Should().Contain("no secret named \"stripe-key\"");
            seen.Should().BeEmpty();
            resolver.ValueRequests.Should().BeEmpty();
        }

        [Fact]
        public async Task An_output_action_name_with_two_secrets_fails_clearly()
        {
            var resolver = new NamedResolver(new() { ["a"] = "1", ["b"] = "2" }, new() { ["stripe-key"] = ["a", "b"] });
            var seen = new List<HttpRequestMessage>();

            var chain = await Processor(resolver, seen).ProcessAsync(Tenant, new FunctionRunEntity { ItemId = "r" },
                [Action("{{secretName.stripe-key}}")], new RetryPolicy { Attempts = 1 });

            chain.Results.Single().Error.Should().Contain("more than one secret");
            seen.Should().BeEmpty();
        }

        [Fact]
        public async Task Output_action_names_are_looked_up_on_every_run()
        {
            var resolver = new NamedResolver(new() { ["s1"] = "v" }, new());
            var seen = new List<HttpRequestMessage>();
            var processor = Processor(resolver, seen);
            var action = Action("{{secretName.stripe-key}}");

            (await processor.ProcessAsync(Tenant, new FunctionRunEntity { ItemId = "r1" }, [action], new RetryPolicy { Attempts = 1 }))
                .AllSucceeded.Should().BeFalse();
            resolver.IdsByName["stripe-key"] = ["s1"];
            (await processor.ProcessAsync(Tenant, new FunctionRunEntity { ItemId = "r2" }, [action], new RetryPolicy { Attempts = 1 }))
                .AllSucceeded.Should().BeTrue();

            resolver.NameRequests.Should().HaveCount(2);
        }

        [Fact]
        public async Task A_name_in_the_runs_result_is_never_resolved()
        {
            var resolver = new NamedResolver(new() { ["s1"] = "v_secret" }, new() { ["stripe-key"] = ["s1"] });
            var seen = new List<HttpRequestMessage>();
            var action = new OutputAction { Id = "a1", Url = "https://hooks.example.com/x", TimeoutSeconds = 5 };

            await Processor(resolver, seen).ProcessAsync(Tenant,
                new FunctionRunEntity { ItemId = "r", Result = "\"{{secretName.stripe-key}}\"" }, [action], new RetryPolicy { Attempts = 1 });

            Capture.Bodies[seen.Single()].Should().Be("\"{{secretName.stripe-key}}\"");
            resolver.NameRequests.Should().BeEmpty();
        }

        // ================================================ "needs secret" on read ====

        private sealed class Catalog : IFunctionSecretCatalog
        {
            public Dictionary<string, string[]> IdsByName { get; } = new();
            public bool Down { get; set; }

            public Task<IReadOnlyDictionary<string, string>> NamesByIdAsync(IReadOnlyCollection<string> secretIds, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

            public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> IdsByNameAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken = default)
            {
                if (Down) throw new InvalidOperationException("down");
                return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(names.ToDictionary(
                    n => n, n => (IReadOnlyList<string>)(IdsByName.TryGetValue(n, out var ids) ? ids : [])));
            }
        }

        private static readonly FunctionEntity Imported = new()
        {
            ItemId = "fn_1",
            Name = "imported",
            Variables =
            [
                new VariableBinding { Key = "STRIPE_KEY", Value = "{{secretName.stripe-key}}" },
                new VariableBinding { Key = "DB_PASSWORD", Value = "{{secretName.db-pass}}" },
            ],
        };

        private static FunctionService ReadService(Catalog catalog)
        {
            var functions = new Mock<IFunctionRepository>();
            functions.Setup(f => f.GetByIdAsync(Tenant, "fn_1", It.IsAny<CancellationToken>())).ReturnsAsync(Imported);
            functions.Setup(f => f.GetAllAsync(Tenant, null, null, null, 0, 20, It.IsAny<CancellationToken>()))
                .ReturnsAsync(((IReadOnlyList<FunctionEntity>)[Imported], 1L));
            var runs = new Mock<IFunctionRunRepository>();
            runs.Setup(r => r.CountSinceByFunctionAsync(Tenant, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, long>());
            return new FunctionService(
                functions.Object, Mock.Of<IFunctionVersionRepository>(), runs.Object,
                Mock.Of<IFunctionAuditService>(), Mock.Of<IFunctionDeletionQueue>(), Mock.Of<IFunctionUsageService>(),
                Mock.Of<IValidator<CreateFunctionRequestDto>>(), Mock.Of<IValidator<UpdateFunctionRequestDto>>(),
                Mock.Of<IValidator<SaveFunctionRequestDto>>(), NullLogger<FunctionService>.Instance,
                new ConfigurationBuilder().Build(), catalog);
        }

        [Fact]
        public async Task The_detail_says_which_secrets_are_missing_or_duplicated()
        {
            var catalog = new Catalog();
            catalog.IdsByName["db-pass"] = ["d1", "d2"];

            var detail = await ReadService(catalog).GetAsync(Tenant, "fn_1");

            detail.MissingSecrets.Should().BeEquivalentTo(["stripe-key"]);
            detail.DuplicateSecrets.Should().BeEquivalentTo(["db-pass"]);
            detail.SecretsUnchecked.Should().BeFalse();
        }

        [Fact]
        public async Task The_warning_clears_itself_once_the_secret_exists()
        {
            var catalog = new Catalog();
            var service = ReadService(catalog);
            (await service.GetAsync(Tenant, "fn_1")).MissingSecrets.Should().Contain("stripe-key");

            catalog.IdsByName["stripe-key"] = ["s1"];
            catalog.IdsByName["db-pass"] = ["d1"];

            var after = await service.GetAsync(Tenant, "fn_1");
            after.MissingSecrets.Should().BeEmpty();
            after.DuplicateSecrets.Should().BeEmpty();
            var (items, _) = await service.GetAllAsync(Tenant, new GetFunctionsRequestDto());
            items.Single().HasUnboundSecrets.Should().BeFalse();
        }

        [Fact]
        public async Task The_list_flags_a_function_with_unbound_secrets()
        {
            var (items, _) = await ReadService(new Catalog()).GetAllAsync(Tenant, new GetFunctionsRequestDto());

            items.Single().HasUnboundSecrets.Should().BeTrue();
            items.Single().UnboundSecrets.Should().BeEquivalentTo(["stripe-key", "db-pass"]);
        }

        [Fact]
        public async Task A_secret_store_outage_never_fails_the_read()
        {
            var detail = await ReadService(new Catalog { Down = true }).GetAsync(Tenant, "fn_1");

            detail.SecretsUnchecked.Should().BeTrue();
            detail.MissingSecrets.Should().BeEmpty();
        }

        // ============================================== tenant in the image name ====

        [Fact]
        public void A_new_image_name_carries_the_tenant()
        {
            FunctionImageRef.For("127.0.0.1:5000", "Tenant_ABC.01", "6f1c2a9e-1b2c-4d5e-8f90-123456789abc", "aaa-bbb")
                .Should().Be("127.0.0.1:5000/fn/tenant-abc-01/6f1c2a9e-1b2c-4d5e-8f90-123456789abc:aaa-bbb");
        }

        [Theory]
        [InlineData("ABC", "abc")]
        [InlineData("--a__b..c--", "a-b-c")]
        [InlineData("äöü", "")]
        [InlineData("   ", "")]
        public void The_tenant_segment_is_registry_safe(string tenant, string expected)
        {
            var segment = FunctionImageRef.TenantSegment(tenant);
            segment.Should().Be(expected);
            if (segment.Length > 0) segment.Should().MatchRegex("^[a-z0-9]+(-[a-z0-9]+)*$");
        }

        [Fact]
        public void A_tenant_that_leaves_nothing_usable_falls_back_to_the_old_name()
        {
            FunctionImageRef.Repository("reg", "äöü", "fn1").Should().Be("reg/fn/fn1");
        }

        [Theory]
        [InlineData("reg:5000/fn/abc:tag", null, "abc")]
        [InlineData("reg:5000/fn/abc@sha256:aa", null, "abc")]
        [InlineData("reg:5000/fn/t1/abc:tag", "t1", "abc")]
        [InlineData("reg:5000/fn/t1/abc@sha256:aa", "t1", "abc")]
        public void Both_image_name_forms_parse(string reference, string? tenant, string functionId)
        {
            FunctionImageRef.TryParse(reference, "reg:5000", out var t, out var id).Should().BeTrue();
            t.Should().Be(tenant);
            id.Should().Be(functionId);
        }

        [Theory]
        [InlineData("other:5000/fn/abc:tag")]
        [InlineData("reg:5000/fn/a/b/c:tag")]
        [InlineData("reg:5000/fn//abc")]
        [InlineData(null)]
        public void Anything_else_is_not_a_function_image(string? reference)
        {
            FunctionImageRef.TryParse(reference, "reg:5000", out _, out _).Should().BeFalse();
        }

        [Fact]
        public void A_version_with_an_old_digest_still_runs_from_it()
        {
            FunctionRunImage.For(new FunctionVersionEntity { ImageDigest = "reg/fn/abc@sha256:old" })
                .Should().Be("reg/fn/abc@sha256:old");
        }

        [Fact]
        public async Task A_new_build_is_queued_under_the_tenant_name()
        {
            var builds = new Mock<IFunctionBuildRepository>();
            builds.Setup(b => b.CreateAsync(Tenant, It.IsAny<FunctionBuildEntity>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var database = new Mock<IDatabase>(MockBehavior.Loose);
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(database.Object);
            var service = new FunctionBuildService(
                builds.Object, Mock.Of<global::Functions.DomainService.Storage.IFunctionArtifactStore>(), cache.Object,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Functions:Registry"] = "reg:5000" }).Build(),
                NullLogger<FunctionBuildService>.Instance);

            await service.EnsureImageAsync(Tenant, new FunctionEntity
            {
                ItemId = "fn_1",
                SourceHash = "h",
                Source = new FunctionSource { IndexJs = "export default async () => 1;", PackageJson = """{"type":"module"}""" },
            }, default, waitSecondsOverride: 0, forceRebuild: true);

            var entry = database.Invocations
                .Where(i => i.Method.Name == "StreamAddAsync" && i.Arguments[0].ToString() == FunctionQueueKeys.BuildsStream)
                .Select(i => i.Arguments[1]).OfType<NameValueEntry[]>().Single();
            entry.Single(e => e.Name == "imageRef").Value.ToString().Should().StartWith("reg:5000/fn/tenant-1/fn_1:");
        }
    }
}
