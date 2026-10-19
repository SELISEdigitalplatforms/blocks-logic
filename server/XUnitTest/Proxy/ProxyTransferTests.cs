using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using Moq;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Services;
using Proxy.DomainService.Utils;
using XUnitTest.TestHelpers;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// Proxy export / import, one proxy per file, identical across environments (2026-10-18). Each environment is
    /// played by its own in-memory store; secrets by a mocked resolver that knows names only.
    /// </summary>
    public class ProxyTransferTests : IDisposable
    {
        private const string Tenant = "tenant-b";
        private const string Id = "0123456789abcdef0123456789abcdef";

        private readonly MemoryProxyStore _store = new();
        private readonly Mock<IProxyVariableResolver> _resolver = new();
        private readonly ProxyTransferService _service;

        public ProxyTransferTests()
        {
            TestBlocksContext.Set(Tenant, "importer-1");
            SecretsAre(new Dictionary<string, int>());
            _service = NewService(_store, _resolver.Object);
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private static ProxyTransferService NewService(MemoryProxyStore store, IProxyVariableResolver? resolver) =>
            new(store, store, TimeProvider.System, Mock.Of<ILogger<ProxyTransferService>>(), resolver);

        /// <summary>name → how many secrets carry exactly that name here.</summary>
        private void SecretsAre(Dictionary<string, int> counts) =>
            _resolver.Setup(r => r.LookupAllIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<string> names, string _, CancellationToken _) =>
                    (IReadOnlyDictionary<string, IReadOnlyList<string>>)names
                        .Where(n => counts.TryGetValue(n, out var c) && c > 0)
                        .ToDictionary(n => n, n => (IReadOnlyList<string>)Enumerable.Range(1, counts[n]).Select(i => $"{n}-id-{i}").ToList()));

        private static ProxyDetailEntity Sample(Action<ProxyDetailEntity>? mutate = null)
        {
            var proxy = new ProxyDetailEntity
            {
                ItemId = Id,
                TenantId = "tenant-a",
                Name = "Stripe Payments",
                Slug = "stripe-payments",
                Upstream = "https://api.stripe.com/v1",
                Methods = new List<HttpMethodType> { HttpMethodType.Get, HttpMethodType.Post },
                Enabled = true,
                Headers = new List<ProxyKeyValue>
                {
                    new() { Key = "X-Api-Key", Value = "{{$VAR.stripe-key}}" },
                    new() { Key = "Authorization", Value = "Bearer sk_live_literal_123456" },
                    new() { Key = "Accept", Value = "application/json" },
                },
                Query = new List<ProxyKeyValue> { new() { Key = "api_token", Value = "literal-query-secret" } },
                Routes = new List<ProxyRouteConfig>
                {
                    new()
                    {
                        Method = HttpMethodType.Post, Path = "charges", UpstreamPath = "charges",
                        Headers = new List<ProxyKeyValue> { new() { Key = "X-Session", Value = "route-literal-secret" } },
                    },
                },
                Resilience = new ProxyResilienceConfig { TimeoutSeconds = 10 },
                RequestsPerMinute = 120,
                CurrentVersion = 7,
                SecretIds = new Dictionary<string, string> { ["stripe-key"] = "env-a-secret-id" },
                CreatedBy = "creator-a",
                LastUpdatedBy = "editor-a",
                OrganizationId = "org-a",
                CreatedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            };
            proxy.Stats.TotalCalls = 99;
            mutate?.Invoke(proxy);
            return proxy;
        }

        private static ProxyExportFile Export(ProxyDetailEntity proxy) =>
            ProxyTransferCodec.BuildFile(proxy, DateTime.UtcNow);

        /// <summary>Edits the file's config and re-signs it, as someone hand-editing a file and fixing the hash would.</summary>
        private static ProxyExportFile Edited(ProxyExportFile file, Action<JsonObject> edit)
        {
            edit(file.Proxy!);
            file.ContentHash = ProxyTransferCodec.Hash(file.Proxy);
            return file;
        }

        // ---------- Export ----------

        [Fact]
        public void Export_LeavesOutEnvironmentFields_AndMasksLiteralCredentials()
        {
            var file = Export(Sample());
            var text = JsonSerializer.Serialize(file, ProxyTransferCodec.Json);

            file.Format.Should().Be("blocks.proxy.export");
            file.SchemaVersion.Should().Be(1);
            file.Proxy!["itemId"]!.GetValue<string>().Should().Be(Id);
            file.Proxy["slug"]!.GetValue<string>().Should().Be("stripe-payments");

            foreach (var excluded in new[] { "tenantId", "secretIds", "stats", "currentVersion", "createdBy", "lastUpdatedBy", "createdDate", "organizationId\":\"org-a", "env-a-secret-id", "tenant-a" })
            {
                text.Should().NotContain(excluded);
            }

            text.Should().NotContain("sk_live_literal_123456").And.NotContain("literal-query-secret").And.NotContain("route-literal-secret");
            text.Should().Contain("{{$VAR.stripe-key}}").And.Contain("application/json");
            file.SecretNames.Should().Equal("stripe-key");
            file.MaskedValues.Should().BeEquivalentTo(
                "header:Authorization", "query:api_token", "route:POST charges:header:X-Session");
        }

        [Fact]
        public void Export_MasksACredentialInTheUpstreamQuery_AndKeepsTheRest()
        {
            var file = Export(Sample(p => p.Upstream = "https://api.example.com/v1?token=abcdefgh12345&region=eu"));

            file.Proxy!["upstream"]!.GetValue<string>().Should().Be("https://api.example.com/v1?token=***&region=eu");
            file.MaskedValues.Should().Contain("upstream");
        }

        [Fact]
        public void Hash_IsStable_AcrossExportsAndKeyOrder_AndChangesWithContent()
        {
            var a = Export(Sample());
            var b = Export(Sample());
            a.ContentHash.Should().Be(b.ContentHash).And.MatchRegex("^[0-9a-f]{64}$");

            var reordered = new JsonObject();
            foreach (var (key, value) in a.Proxy!.Reverse()) reordered[key] = value?.DeepClone();
            ProxyTransferCodec.Hash(reordered).Should().Be(a.ContentHash);

            reordered["name"] = "Other";
            ProxyTransferCodec.Hash(reordered).Should().NotBe(a.ContentHash);
        }

        [Fact]
        public async Task Export_UnknownId_Is404()
        {
            var result = await _service.ExportAsync(Tenant, Id);
            result.HttpStatus.Should().Be(404);
        }

        // ---------- Preview ----------

        [Fact]
        public async Task Preview_UnknownId_FreeSlug_IsNew_AndWritesNothing()
        {
            var result = await _service.PreviewAsync(Tenant, Export(Sample()));

            result.HttpStatus.Should().Be(200);
            result.Data!.Status.Should().Be("new");
            result.Data.CurrentVersion.Should().Be(1);
            result.Data.HistoryExists.Should().BeFalse();
            result.Data.MaskedValues.Should().HaveCount(3);
            _store.Proxies.Should().BeEmpty();
            _store.Versions.Should().BeEmpty();
        }

        [Fact]
        public async Task Preview_DeletedIdWithHistory_FlagsIt_AndNumbersAfterTheLastVersion()
        {
            _store.Versions.Add(VersionRow(Id, 4));
            _store.Versions.Add(VersionRow(Id, 9));

            var result = await _service.PreviewAsync(Tenant, Export(Sample()));

            result.Data!.Status.Should().Be("new");
            result.Data.HistoryExists.Should().BeTrue();
            result.Data.CurrentVersion.Should().Be(10);
        }

        [Fact]
        public async Task Preview_SameConfig_IsSame()
        {
            _store.Seed(Sample(p => p.TenantId = Tenant));

            var result = await _service.PreviewAsync(Tenant, Export(Sample()));

            result.Data!.Status.Should().Be("same");
            result.Data.Changes.Should().BeEmpty();
        }

        [Fact]
        public async Task Preview_DifferentConfig_IsChanged_WithFieldDiff_AndNoStoredSecretInTheAnswer()
        {
            _store.Seed(Sample(p =>
            {
                p.TenantId = Tenant;
                p.Name = "Stripe Old";
                p.Headers[1].Value = "Bearer sk_live_ENV_B_ONLY_999";
            }));

            var result = await _service.PreviewAsync(Tenant, Export(Sample()));

            result.Data!.Status.Should().Be("changed");
            result.Data.CurrentVersion.Should().Be(7);
            result.Data.Changes.Should().ContainSingle(c => c.Field == "name" && c.Mine == "Stripe Old" && c.Incoming == "Stripe Payments");
            // Different literal values under a credential key are both *** — not a change, and never shown.
            result.Data.Changes.Should().NotContain(c => c.Field == "header:Authorization");
            JsonSerializer.Serialize(result.Data).Should().NotContain("sk_live_ENV_B_ONLY_999").And.NotContain("sk_live_literal_123456");
        }

        [Fact]
        public async Task Preview_SlugHeldByAnotherId_IsBlocked()
        {
            _store.Seed(Sample(p => { p.TenantId = Tenant; p.ItemId = "ffffffffffffffffffffffffffffffff"; }));

            var result = await _service.PreviewAsync(Tenant, Export(Sample()));

            result.Data!.Status.Should().Be("blocked");
            result.Data.ReasonCode.Should().Be("slugConflict");
            result.Data.Reason.Should().Contain("stripe-payments");
        }

        [Fact]
        public async Task Preview_SameIdWithAnotherSlug_IsBlocked()
        {
            _store.Seed(Sample(p => { p.TenantId = Tenant; p.Slug = "stripe-old"; }));

            var result = await _service.PreviewAsync(Tenant, Export(Sample()));

            result.Data!.Status.Should().Be("blocked");
            result.Data.ReasonCode.Should().Be("slugMismatch");
        }

        [Fact]
        public async Task Preview_ReportsMissingAndDuplicateSecretNames()
        {
            SecretsAre(new() { ["stripe-key"] = 2 });
            var file = Export(Sample(p => p.Query.Add(new ProxyKeyValue { Key = "org", Value = "{{$VAR.org-id}}" })));

            var result = await _service.PreviewAsync(Tenant, file);

            result.Data!.MissingSecrets.Should().Equal("org-id");
            result.Data.DuplicateSecrets.Should().Equal("stripe-key");
        }

        [Fact]
        public async Task Preview_SecretStoreDown_SaysSo_AndStillAnswers()
        {
            _resolver.Setup(r => r.LookupAllIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("vault down"));

            var result = await _service.PreviewAsync(Tenant, Export(Sample()));

            result.Data!.Status.Should().Be("new");
            result.Data.SecretCheckUnavailable.Should().BeTrue();
        }

        // ---------- Refused files ----------

        [Fact]
        public async Task Preview_OversizeConfig_Is413()
        {
            var file = Edited(Export(Sample()), p => p["name"] = new string('a', ProxyTransferCodec.MaxConfigBytes));
            var result = await _service.PreviewAsync(Tenant, file);
            result.HttpStatus.Should().Be(413);
        }

        [Fact]
        public async Task Preview_UnknownFormat_Is400()
        {
            var file = Export(Sample());
            file.Format = "blocks.function.export";
            var result = await _service.PreviewAsync(Tenant, file);
            result.HttpStatus.Should().Be(400);
            result.Errors.Should().ContainKey("format");
        }

        [Fact]
        public async Task Preview_UnknownSchemaVersion_Is400()
        {
            var file = Export(Sample());
            file.SchemaVersion = 2;
            var result = await _service.PreviewAsync(Tenant, file);
            result.HttpStatus.Should().Be(400);
            result.Errors.Should().ContainKey("schemaVersion");
        }

        [Fact]
        public async Task Preview_EditedWithoutNewHash_Is400()
        {
            var file = Export(Sample());
            file.Proxy!["upstream"] = "https://evil.example.com";
            var result = await _service.PreviewAsync(Tenant, file);
            result.HttpStatus.Should().Be(400);
            result.Errors.Should().ContainKey("contentHash");
        }

        [Theory]
        [InlineData("not-an-id")]
        [InlineData("0123456789ABCDEF0123456789ABCDEF")]
        [InlineData("0123456789abcdef0123456789abcde")]
        public async Task Preview_BadItemId_Is400(string itemId)
        {
            var file = Edited(Export(Sample()), p => p["itemId"] = itemId);
            var result = await _service.PreviewAsync(Tenant, file);
            result.HttpStatus.Should().Be(400);
            result.Errors.Should().ContainKey("itemId");
        }

        [Fact]
        public async Task Preview_BadSlug_Is400()
        {
            var file = Edited(Export(Sample()), p => p["slug"] = "Not A Slug");
            var result = await _service.PreviewAsync(Tenant, file);
            result.Errors.Should().ContainKey("slug");
        }

        [Theory]
        [InlineData("http://api.stripe.com")]
        [InlineData("https://user:pass@api.stripe.com")]
        [InlineData("https://127.0.0.1/admin")]
        public async Task Preview_ConfigTheValidatorRefuses_Is400(string upstream)
        {
            var file = Edited(Export(Sample()), p => p["upstream"] = upstream);
            var result = await _service.PreviewAsync(Tenant, file);
            result.HttpStatus.Should().Be(400);
            result.Errors.Should().ContainKey("upstream");
        }

        [Fact]
        public async Task Preview_ReservedHeader_Is400()
        {
            var file = Edited(Export(Sample()), p => p["headers"]!.AsArray().Add(new JsonObject { ["key"] = "Host", ["value"] = "internal" }));
            var result = await _service.PreviewAsync(Tenant, file);
            result.HttpStatus.Should().Be(400);
        }

        [Fact]
        public async Task Apply_RefusedFile_WritesNothing()
        {
            var file = Export(Sample());
            file.ContentHash = new string('0', 64);
            var result = await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto { File = file });
            result.HttpStatus.Should().Be(400);
            _store.Proxies.Should().BeEmpty();
        }

        // ---------- Apply: new ----------

        [Fact]
        public async Task Apply_New_KeepsIdAndSlug_TakesCallerTenantAndImporter_AndWritesOneVersion()
        {
            SecretsAre(new() { ["stripe-key"] = 1 });

            var result = await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto { File = Export(Sample()) });

            result.HttpStatus.Should().Be(200);
            result.Data!.Applied.Should().BeTrue();
            var saved = _store.Proxies.Single().Value;
            saved.ItemId.Should().Be(Id);
            saved.Slug.Should().Be("stripe-payments");
            saved.TenantId.Should().Be(Tenant);
            saved.CreatedBy.Should().Be("importer-1");
            saved.LastUpdatedBy.Should().Be("importer-1");
            saved.CurrentVersion.Should().Be(1);
            saved.Stats.TotalCalls.Should().Be(0);
            saved.SecretIds.Should().Equal(new Dictionary<string, string> { ["stripe-key"] = "stripe-key-id-1" });
            saved.Headers.Single(h => h.Key == "Authorization").Value.Should().Be("***", "nothing to restore on a new proxy; the owner sets it");

            var version = _store.Versions.Single();
            version.VersionNumber.Should().Be(1);
            version.Kind.Should().Be(ProxyVersionKind.Create);
            version.ChangeSummary.Should().Be("Imported from file");
        }

        [Fact]
        public async Task Apply_New_AfterDeletedHistory_NumbersAfterIt()
        {
            _store.Versions.Add(VersionRow(Id, 5));

            await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto { File = Export(Sample()) });

            _store.Proxies[Id].CurrentVersion.Should().Be(6);
            _store.Versions.Select(v => v.VersionNumber).Should().Equal(5, 6);
        }

        [Fact]
        public async Task Apply_New_MissingOrDuplicateSecret_DoesNotRefuse_AndLeavesThemUnbound()
        {
            SecretsAre(new() { ["org-id"] = 2 });
            var file = Export(Sample(p => p.Query.Add(new ProxyKeyValue { Key = "org", Value = "{{$VAR.org-id}}" })));

            var result = await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto { File = file });

            result.HttpStatus.Should().Be(200);
            result.Data!.MissingSecrets.Should().Equal("stripe-key");
            result.Data.DuplicateSecrets.Should().Equal("org-id");
            _store.Proxies[Id].SecretIds.Should().BeEmpty();
        }

        [Fact]
        public async Task NormalCreate_WithMissingSecret_IsStillRefused()
        {
            _resolver.Setup(r => r.LookupIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), Tenant, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, string>());
            var proxyService = new ProxyService(
                _store, _store, Mock.Of<IProxyExecutionRepository>(), TimeProvider.System,
                Mock.Of<ILogger<ProxyService>>(), _resolver.Object);

            var result = await proxyService.CreateAsync(Tenant, new ProxyCreateRequestDto
            {
                Name = "OpenAI",
                Upstream = "https://api.openai.com",
                Methods = new List<string> { "POST" },
                Headers = new List<ProxyKeyValueInputDto> { new() { Key = "Authorization", Value = "Bearer {{$VAR.nope}}" } },
            });

            result.HttpStatus.Should().Be(400);
            _store.Proxies.Should().BeEmpty();
        }

        [Fact]
        public async Task Apply_New_InsertRace_Is409Blocked()
        {
            _store.ThrowDuplicateOnInsert = true;

            var result = await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto { File = Export(Sample()) });

            result.HttpStatus.Should().Be(409);
            result.Code.Should().Be(ProxyErrorCodes.ImportBlocked);
            _store.Versions.Should().BeEmpty();
        }

        [Fact]
        public async Task Apply_Replay_IsIdempotent()
        {
            var file = Export(Sample());
            await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto { File = file });

            var again = await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto { File = Export(Sample()) });

            again.HttpStatus.Should().Be(200);
            again.Data!.Status.Should().Be("same");
            again.Data.Applied.Should().BeFalse();
            _store.Versions.Should().HaveCount(1);
            _store.Proxies[Id].CurrentVersion.Should().Be(1);
        }

        [Fact]
        public async Task Apply_Blocked_Is409_WithTheReason()
        {
            _store.Seed(Sample(p => { p.TenantId = Tenant; p.ItemId = "ffffffffffffffffffffffffffffffff"; }));

            var result = await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto { File = Export(Sample()) });

            result.HttpStatus.Should().Be(409);
            result.Code.Should().Be(ProxyErrorCodes.ImportBlocked);
            result.Message.Should().Contain("stripe-payments");
            result.Data!.ReasonCode.Should().Be("slugConflict");
        }

        // ---------- Apply: changed ----------

        private ProxyDetailEntity SeedTarget(Action<ProxyDetailEntity>? mutate = null)
        {
            var target = Sample(p =>
            {
                p.TenantId = Tenant;
                p.Name = "Stripe Old";
                p.Enabled = false;
                p.Headers[1].Value = "Bearer sk_live_ENV_B_ONLY_999";
                p.Headers.Add(new ProxyKeyValue { Key = "X-Env", Value = "b" });
                p.SecretIds = new Dictionary<string, string> { ["stripe-key"] = "env-b-id" };
                p.Stats.TotalCalls = 5;
                p.CreatedBy = "creator-b";
                mutate?.Invoke(p);
            });
            _store.Seed(target);
            return target;
        }

        [Fact]
        public async Task Apply_TakeIncoming_TakesTheFile_ButKeepsThisEnvironmentsMaskedValues()
        {
            SeedTarget();
            SecretsAre(new() { ["stripe-key"] = 1 });

            var result = await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto
            {
                File = Export(Sample()), Resolution = "takeIncoming", ExpectedVersion = 7,
            });

            result.HttpStatus.Should().Be(200);
            var saved = _store.Proxies[Id];
            saved.Name.Should().Be("Stripe Payments");
            saved.Enabled.Should().BeTrue("Enabled comes from the file");
            saved.Headers.Should().NotContain(h => h.Key == "X-Env");
            saved.Headers.Single(h => h.Key == "Authorization").Value.Should().Be("Bearer sk_live_ENV_B_ONLY_999");
            saved.Query.Single().Value.Should().Be("literal-query-secret");
            saved.Routes.Single().Headers!.Single().Value.Should().Be("route-literal-secret");
            saved.CurrentVersion.Should().Be(8);
            saved.CreatedBy.Should().Be("creator-b");
            saved.Stats.TotalCalls.Should().Be(5, "stats are owned by the flush, never by a config write");
            saved.SecretIds.Should().Equal(new Dictionary<string, string> { ["stripe-key"] = "stripe-key-id-1" });

            var version = _store.Versions.Single();
            version.Kind.Should().Be(ProxyVersionKind.ConfigUpdate);
            version.VersionNumber.Should().Be(8);
            version.ChangeSummary.Should().StartWith("Imported from file");
        }

        [Fact]
        public async Task Apply_TakeIncoming_RestoresAMaskedUpstreamQuery_WhenTheRestMatches()
        {
            SeedTarget(p => p.Upstream = "https://api.stripe.com/v1?token=env-b-token-123");
            var file = Export(Sample(p => p.Upstream = "https://api.stripe.com/v1?token=env-a-token-456"));

            await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto { File = file, Resolution = "takeIncoming" });

            _store.Proxies[Id].Upstream.Should().Be("https://api.stripe.com/v1?token=env-b-token-123");
        }

        [Fact]
        public async Task Apply_Merge_TakesOnlyTheChosenFields()
        {
            SeedTarget();

            var preview = await _service.PreviewAsync(Tenant, Export(Sample()));
            preview.Data!.Changes.Select(c => c.Field).Should().Contain(new[] { "name", "enabled", "header:X-Env" });

            var result = await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto
            {
                File = Export(Sample()),
                Resolution = "merge",
                FieldChoices = new Dictionary<string, string> { ["name"] = "incoming", ["enabled"] = "mine", ["header:Made-Up"] = "incoming" },
                ExpectedVersion = 7,
            });

            result.HttpStatus.Should().Be(200);
            var saved = _store.Proxies[Id];
            saved.Name.Should().Be("Stripe Payments");
            saved.Enabled.Should().BeFalse();
            saved.Headers.Should().Contain(h => h.Key == "X-Env");
            saved.Headers.Should().NotContain(h => h.Key == "Made-Up");
            saved.Headers.Single(h => h.Key == "Authorization").Value.Should().Be("Bearer sk_live_ENV_B_ONLY_999");
            _store.Versions.Single().Changes.Select(c => c.Field).Should().Equal("name");
        }

        [Fact]
        public async Task Apply_Merge_ResilienceRowsAreTakenFromTheFile()
        {
            SeedTarget(p => p.Resilience = null);

            await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto
            {
                File = Export(Sample()), Resolution = "merge",
                FieldChoices = new Dictionary<string, string> { ["resilience:timeout"] = "incoming" },
            });

            _store.Proxies[Id].Resilience!.TimeoutSeconds.Should().Be(10);
        }

        [Fact]
        public async Task Apply_Merge_NothingChosen_WritesNothing()
        {
            SeedTarget();

            var result = await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto
            {
                File = Export(Sample()), Resolution = "merge", FieldChoices = new Dictionary<string, string>(),
            });

            result.Data!.Applied.Should().BeFalse();
            _store.Versions.Should().BeEmpty();
        }

        [Fact]
        public async Task Apply_Merge_BadChoice_Is400()
        {
            SeedTarget();

            var result = await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto
            {
                File = Export(Sample()), Resolution = "merge",
                FieldChoices = new Dictionary<string, string> { ["name"] = "theirs" },
            });

            result.HttpStatus.Should().Be(400);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("overwrite")]
        public async Task Apply_Changed_WithoutAKnownResolution_Is400(string? resolution)
        {
            SeedTarget();
            var result = await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto { File = Export(Sample()), Resolution = resolution });
            result.HttpStatus.Should().Be(400);
            result.Errors.Should().ContainKey("resolution");
        }

        [Fact]
        public async Task Apply_KeepMine_WritesNothing()
        {
            SeedTarget();

            var result = await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto { File = Export(Sample()), Resolution = "keepMine" });

            result.HttpStatus.Should().Be(200);
            result.Data!.Applied.Should().BeFalse();
            _store.Proxies[Id].Name.Should().Be("Stripe Old");
            _store.Versions.Should().BeEmpty();
        }

        [Fact]
        public async Task Apply_StaleExpectedVersion_Is409()
        {
            SeedTarget();

            var result = await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto
            {
                File = Export(Sample()), Resolution = "takeIncoming", ExpectedVersion = 6,
            });

            result.HttpStatus.Should().Be(409);
            result.Code.Should().Be(ProxyErrorCodes.VersionConflict);
            _store.Proxies[Id].Name.Should().Be("Stripe Old");
        }

        [Fact]
        public async Task Apply_LostRaceOnSave_Is409_AndWritesNoHistory()
        {
            SeedTarget();
            _store.SaveLosesRace = true;

            var result = await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto
            {
                File = Export(Sample()), Resolution = "takeIncoming", ExpectedVersion = 7,
            });

            result.HttpStatus.Should().Be(409);
            result.Code.Should().Be(ProxyErrorCodes.VersionConflict);
            _store.Versions.Should().BeEmpty();
        }

        [Fact]
        public async Task Apply_Changed_DropsTheIdOfANameThatNoLongerResolves()
        {
            SeedTarget();
            SecretsAre(new Dictionary<string, int>());

            await _service.ApplyAsync(Tenant, new ProxyImportApplyRequestDto { File = Export(Sample()), Resolution = "takeIncoming" });

            _store.Proxies[Id].SecretIds.Should().BeEmpty();
        }

        // ---------- Round trip ----------

        [Fact]
        public async Task RoundTrip_ExportImportExport_GivesTheSameFile_ExceptExportedAt()
        {
            // Environment A holds the proxy; B is empty. Credentials are refs here so nothing is masked away.
            var envA = new MemoryProxyStore();
            envA.Seed(Sample(p =>
            {
                p.Headers.RemoveAt(1);
                p.Query.Clear();
                p.Routes[0].Headers = new List<ProxyKeyValue> { new() { Key = "X-Session", Value = "{{$VAR.session}}" } };
                p.MethodConfigs = new List<ProxyMethodConfig>
                {
                    new() { Method = HttpMethodType.Post, Headers = new List<ProxyKeyValue> { new() { Key = "Idempotency", Value = "on" } } },
                };
                p.ResponseMode = ProxyResponseMode.Select;
                p.ResponseInclude = new List<string> { "id", "status" };
                p.Access = new EndpointAccessPolicy
                {
                    Kind = EndpointAccessKind.BlocksToken,
                    Roles = new EndpointAccessRule { Mode = "all", Values = new List<string> { "admin" } },
                };
            }));
            var serviceA = NewService(envA, null);
            var envB = new MemoryProxyStore();
            var serviceB = NewService(envB, _resolver.Object);

            var first = (await serviceA.ExportAsync("tenant-a", Id)).Data!;
            var firstText = JsonSerializer.Serialize(first, ProxyTransferCodec.Json);
            var imported = await serviceB.ApplyAsync(Tenant, new ProxyImportApplyRequestDto
            {
                File = JsonSerializer.Deserialize<ProxyExportFile>(firstText, ProxyTransferCodec.Json),
            });
            imported.Data!.Status.Should().Be("new");

            var second = (await serviceB.ExportAsync(Tenant, Id)).Data!;

            first.ExportedAt = second.ExportedAt = null;
            JsonSerializer.Serialize(second, ProxyTransferCodec.Json).Should().Be(JsonSerializer.Serialize(first, ProxyTransferCodec.Json));
            envB.Proxies[Id].TenantId.Should().Be(Tenant);
        }

        // ---------- "Needs secret" on read ----------

        [Fact]
        public async Task DetailRead_ListsUnboundNamesThatAreMissingOrAmbiguous_AndListHintsAtThem()
        {
            SecretsAre(new() { ["stripe-key"] = 1, ["dup"] = 2 });
            _store.Seed(Sample(p =>
            {
                p.TenantId = Tenant;
                p.SecretIds = new Dictionary<string, string>();
                p.Query = new List<ProxyKeyValue>
                {
                    new() { Key = "a", Value = "{{$VAR.missing}}" },
                    new() { Key = "b", Value = "{{$VAR.dup}}" },
                };
            }));
            var proxyService = new ProxyService(
                _store, _store, Mock.Of<IProxyExecutionRepository>(), TimeProvider.System,
                Mock.Of<ILogger<ProxyService>>(), _resolver.Object);

            var detail = await proxyService.GetAsync(Tenant, new ProxyGetRequestDto { ItemId = Id });
            var list = await proxyService.GetAllAsync(Tenant, new ProxyGetAllRequestDto());

            detail.Data!.MissingSecrets.Should().Equal("missing", "dup");
            detail.Data.DuplicateSecrets.Should().Equal("dup");
            list.Data!.Single().HasUnboundSecrets.Should().BeTrue();
        }

        [Fact]
        public async Task DetailRead_BoundNames_CostNoLookup_AndShowNothing()
        {
            _store.Seed(Sample(p => p.TenantId = Tenant));
            var proxyService = new ProxyService(
                _store, _store, Mock.Of<IProxyExecutionRepository>(), TimeProvider.System,
                Mock.Of<ILogger<ProxyService>>(), _resolver.Object);

            var detail = await proxyService.GetAsync(Tenant, new ProxyGetRequestDto { ItemId = Id });
            var list = await proxyService.GetAllAsync(Tenant, new ProxyGetAllRequestDto());

            detail.Data!.MissingSecrets.Should().BeEmpty();
            list.Data!.Single().HasUnboundSecrets.Should().BeFalse();
            _resolver.Verify(r => r.LookupAllIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ---------- helpers ----------

        private static ProxyVersionEntity VersionRow(string proxyId, int number) => new()
        {
            ItemId = Guid.NewGuid().ToString("N"),
            TenantId = Tenant,
            ProxyId = proxyId,
            VersionNumber = number,
            Snapshot = new ProxyConfigSnapshot(),
        };

        /// <summary>
        /// One environment's database: rows are stored as BSON copies, so the service can never alias what is
        /// "in the database", and config writes keep the stored Stats exactly like the real $set.
        /// </summary>
        internal sealed class MemoryProxyStore : IProxyRepository, IProxyVersionRepository
        {
            public Dictionary<string, ProxyDetailEntity> Proxies { get; } = new(StringComparer.Ordinal);

            public List<ProxyVersionEntity> Versions { get; } = new();

            public bool SaveLosesRace { get; set; }

            public bool ThrowDuplicateOnInsert { get; set; }

            public void Seed(ProxyDetailEntity proxy) => Proxies[proxy.ItemId] = Copy(proxy);

            private static ProxyDetailEntity Copy(ProxyDetailEntity proxy) =>
                BsonSerializer.Deserialize<ProxyDetailEntity>(proxy.ToBsonDocument());

            public Task<ProxyDetailEntity?> GetAsync(string tenantId, string itemId) =>
                Task.FromResult(Proxies.TryGetValue(itemId, out var p) ? Copy(p) : null);

            public Task<ProxyDetailEntity?> GetBySlugAsync(string tenantId, string slug) =>
                Task.FromResult(Proxies.Values.Where(p => p.Slug == slug).Select(Copy).FirstOrDefault());

            public Task<List<ProxyDetailEntity>> GetAllAsync(string tenantId, string? search, bool? isActive, int pageSize, int pageNumber) =>
                Task.FromResult(Proxies.Values.Select(Copy).ToList());

            public Task<long> CountAsync(string tenantId, string? search, bool? isActive) => Task.FromResult((long)Proxies.Count);

            public Task InsertAsync(ProxyDetailEntity proxy)
            {
                if (ThrowDuplicateOnInsert || Proxies.ContainsKey(proxy.ItemId) || Proxies.Values.Any(p => p.Slug == proxy.Slug))
                {
                    throw DuplicateKey();
                }

                Proxies[proxy.ItemId] = Copy(proxy);
                return Task.CompletedTask;
            }

            public Task<bool> SaveConfigAsync(ProxyDetailEntity proxy, int expectedVersion)
            {
                if (SaveLosesRace || !Proxies.TryGetValue(proxy.ItemId, out var stored) || stored.CurrentVersion != expectedVersion)
                {
                    return Task.FromResult(false);
                }

                var copy = Copy(proxy);
                copy.Stats = stored.Stats;
                Proxies[proxy.ItemId] = copy;
                return Task.FromResult(true);
            }

            public Task<bool> DeleteAsync(string tenantId, string itemId, int expectedVersion) => Task.FromResult(Proxies.Remove(itemId));

            public Task ApplyStatsDeltasAsync(string tenantId, IReadOnlyList<ProxyStatsDelta> deltas, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;

            public Task InsertAsync(ProxyVersionEntity version)
            {
                Versions.Add(version);
                return Task.CompletedTask;
            }

            Task<ProxyVersionEntity?> IProxyVersionRepository.GetAsync(string tenantId, string versionId) =>
                Task.FromResult(Versions.FirstOrDefault(v => v.ItemId == versionId));

            public Task<List<ProxyVersionEntity>> GetForProxyAsync(string tenantId, string proxyId, int pageSize, int pageNumber) =>
                Task.FromResult(Versions.Where(v => v.ProxyId == proxyId).OrderByDescending(v => v.VersionNumber)
                    .Skip(pageNumber * pageSize).Take(pageSize).ToList());

            public Task<long> CountForProxyAsync(string tenantId, string proxyId) =>
                Task.FromResult((long)Versions.Count(v => v.ProxyId == proxyId));

            private static MongoWriteException DuplicateKey()
            {
                var writeError = (WriteError)Activator.CreateInstance(
                    typeof(WriteError), BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
                    args: new object?[] { ServerErrorCategory.DuplicateKey, 11000, "E11000 duplicate key error", null },
                    culture: null)!;
                var connectionId = new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));
                return new MongoWriteException(connectionId, writeError, writeConcernError: null, new InvalidOperationException("dup"));
            }
        }
    }
}
