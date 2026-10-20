using System.Reflection;
using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using BlocksTemplate.Api.Controllers;
using FluentAssertions;
using Functions.DomainService.Dtos;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Functions.DomainService.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Function export / import, one function per file, identical across environments (2026-10-18): the same id in
    /// every tenant, secret names instead of ids, nothing environment-specific in the file, a conditional update, and
    /// an automatic build + deploy that never undoes the import.
    /// </summary>
    public class FunctionTransferTests
    {
        private const string TenantA = "tenant-a";
        private const string TenantB = "tenant-b";
        private const string FunctionId = "6f1c2a9e-1b2c-4d5e-8f90-123456789abc";
        private const string Importer = "user-importer";

        // ------------------------------------------------------------- fakes ----

        /// <summary>An in-memory function store with the repository's real semantics for what import uses.</summary>
        internal sealed class InMemoryFunctions : IFunctionRepository
        {
            public Dictionary<string, FunctionEntity> Live { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, FunctionEntity> Tombstoned { get; } = new(StringComparer.Ordinal);

            /// <summary>Ids that exist but the data access policy hides from reads (insert still collides).</summary>
            public HashSet<string> Hidden { get; } = new(StringComparer.Ordinal);

            public int Inserts { get; private set; }
            public int Updates { get; private set; }

            public Task<FunctionEntity?> GetByIdAsync(string tenantId, string functionId, CancellationToken cancellationToken = default) =>
                Task.FromResult(Live.TryGetValue(functionId, out var f) ? Clone(f) : null);

            public Task<FunctionEntity?> GetDeletedAsync(string tenantId, string functionId, CancellationToken cancellationToken = default) =>
                Task.FromResult(Tombstoned.TryGetValue(functionId, out var f) ? f : null);

            public Task CreateAsync(string tenantId, FunctionEntity function, CancellationToken cancellationToken = default)
            {
                if (Live.ContainsKey(function.ItemId) || Tombstoned.ContainsKey(function.ItemId) || Hidden.Contains(function.ItemId))
                {
                    throw DuplicateKey();
                }
                Inserts++;
                Live[function.ItemId] = Clone(function);
                return Task.CompletedTask;
            }

            public Task<bool> UpdateImportedAsync(
                string tenantId, string functionId, DateTime expectedLastUpdatedDate, string name, string? description,
                FunctionSource source, string sourceHash, FunctionLimits limits, RetryPolicy retry, TriggerConfig trigger,
                List<OutputAction> outputActions, List<VariableBinding> variables, string? actorId,
                CancellationToken cancellationToken = default)
            {
                if (!Live.TryGetValue(functionId, out var f) || f.LastUpdatedDate != expectedLastUpdatedDate)
                {
                    return Task.FromResult(false);
                }
                Updates++;
                f.Name = name;
                f.Description = description;
                f.Source = source;
                f.SourceHash = sourceHash;
                f.Limits = limits;
                f.Retry = retry;
                f.Trigger = trigger;
                f.OutputActions = outputActions;
                f.Variables = variables;
                f.LastUpdatedDate = f.LastUpdatedDate.AddSeconds(1);
                f.LastUpdatedBy = actorId;
                return Task.FromResult(true);
            }

            private static FunctionEntity Clone(FunctionEntity f) =>
                JsonSerializer.Deserialize<FunctionEntity>(JsonSerializer.Serialize(f))!;

            private static MongoWriteException DuplicateKey()
            {
                // MongoWriteException has no public constructor; build one the way the driver does.
                var writeError = (WriteError)Activator.CreateInstance(
                    typeof(WriteError), BindingFlags.NonPublic | BindingFlags.Instance, null,
                    [ServerErrorCategory.DuplicateKey, 11000, "E11000 duplicate key", null], null)!;
                var connectionId = new MongoDB.Driver.Core.Connections.ConnectionId(
                    new MongoDB.Driver.Core.Servers.ServerId(
                        new MongoDB.Driver.Core.Clusters.ClusterId(), new System.Net.DnsEndPoint("localhost", 27017)));
                return new MongoWriteException(connectionId, writeError, null, null);
            }

            public Task<(IReadOnlyList<FunctionEntity> Items, long TotalCount)> GetAllAsync(string tenantId, string? searchKey, string? status, string? sortBy, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task UpdateDetailsAsync(string tenantId, string functionId, string name, string? description, string? actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task UpdateSourceAndConfigAsync(string tenantId, string functionId, FunctionSource source, string sourceHash, FunctionLimits limits, RetryPolicy retry, TriggerConfig trigger, List<OutputAction> outputActions, List<VariableBinding> variables, string? actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task SetActiveVersionAsync(string tenantId, string functionId, string versionId, int versionNumber, DateTime deployedAt, string? actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task MoveActiveVersionAsync(string tenantId, string functionId, string versionId, string? actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task RecordRunStartedAsync(string tenantId, string functionId, DateTime startedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task ImportRunCountersAsync(string tenantId, string functionId, long totalRuns, DateTime? lastRunAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<bool> MarkDeletedAsync(string tenantId, string functionId, FunctionDeletion deletion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<IReadOnlyList<string>> GetDeletedIdsAsync(string tenantId, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task RecordPurgePassAsync(string tenantId, string functionId, FunctionPurgeReport report, DateTime at, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<bool> DeleteTombstoneAsync(string tenantId, string functionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }

        /// <summary>One environment's secret catalog: id → name. Holds no values at all, which is the point.</summary>
        internal sealed class FakeCatalog : IFunctionSecretCatalog
        {
            public Dictionary<string, string> NameById { get; } = new(StringComparer.Ordinal);
            public bool Down { get; set; }
            public int NameLookups { get; private set; }

            public Task<IReadOnlyDictionary<string, string>> NamesByIdAsync(IReadOnlyCollection<string> secretIds, CancellationToken cancellationToken = default)
            {
                if (Down) throw new InvalidOperationException("store down");
                IReadOnlyDictionary<string, string> found = secretIds.Where(NameById.ContainsKey)
                    .ToDictionary(id => id, id => NameById[id], StringComparer.Ordinal);
                return Task.FromResult(found);
            }

            public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> IdsByNameAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken = default)
            {
                NameLookups++;
                if (Down) throw new InvalidOperationException("store down");
                IReadOnlyDictionary<string, IReadOnlyList<string>> found = names.ToDictionary(
                    n => n,
                    n => (IReadOnlyList<string>)NameById.Where(kv => kv.Value == n).Select(kv => kv.Key).ToList(),
                    StringComparer.Ordinal);
                return Task.FromResult(found);
            }
        }

        private sealed class Env
        {
            public InMemoryFunctions Store { get; } = new();
            public FakeCatalog Catalog { get; } = new();
            public Mock<IFunctionDeploymentService> Deploy { get; } = new();
            public Mock<IFunctionAuditService> Audit { get; } = new();
            public List<(string Action, string? Detail)> AuditRows { get; } = [];

            public Env()
            {
                Deploy.Setup(d => d.DeployAsync(It.IsAny<string>(), It.IsAny<DeployFunctionRequestDto>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DeployOutcome(null, "build-1", "Building"));
                Audit.Setup(a => a.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                    .Callback((string _, string _, string action, string? _, string? _, object? detail, CancellationToken _) =>
                        AuditRows.Add((action, detail is null ? null : JsonSerializer.Serialize(detail))))
                    .Returns(Task.CompletedTask);
            }

            public FunctionTransferService Service() => new(
                Store, Catalog, Audit.Object, Deploy.Object,
                new SaveFunctionRequestValidator(), new UpdateFunctionRequestValidator(),
                NullLogger<FunctionTransferService>.Instance);
        }

        private const string StripeValue = "sk_live_NEVER_IN_A_FILE";

        private static FunctionEntity StoredFunction(string? secretId = "sec-a-1", bool allowScripts = false) => new()
        {
            ItemId = FunctionId,
            Name = "orders",
            Description = "Order hook",
            Status = FunctionStatus.Live,
            ActiveVersionId = "ver-7",
            LastVersionNumber = 7,
            LastDeployedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            TotalRuns = 99,
            LastRunAt = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc),
            CreatedBy = "creator-a",
            LastUpdatedBy = "editor-a",
            OrganizationId = "org-a",
            CreatedDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedDate = new DateTime(2026, 10, 3, 0, 0, 0, 123, DateTimeKind.Utc),
            Source = new FunctionSource
            {
                IndexJs = "export default async (ctx) => ({ ok: true });",
                PackageJson = """{"type":"module"}""",
                LockJson = null,
                AllowInstallScripts = allowScripts,
            },
            SourceHash = "hash-a",
            Limits = new FunctionLimits().Clamp(),
            Retry = RetryPolicy.Fixed,
            Trigger = new TriggerConfig { AuthMode = AuthMode.Public, HttpMethods = ["POST"] },
            OutputActions =
            [
                new OutputAction
                {
                    Id = "oa1",
                    Url = "https://hooks.example.com/orders",
                    Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {{{{secret.{secretId}}}}}", ["X-Trace"] = "1" },
                },
            ],
            Variables =
            [
                new VariableBinding { Key = "STRIPE_KEY", Value = $"{{{{secret.{secretId}}}}}" },
                new VariableBinding { Key = "REGION", Value = "eu" },
            ],
        };

        private static Env SourceEnv(FunctionEntity? function = null)
        {
            var env = new Env();
            env.Catalog.NameById["sec-a-1"] = "stripe-key";
            var f = function ?? StoredFunction();
            env.Store.Live[f.ItemId] = f;
            return env;
        }

        private static JsonElement AsElement(FunctionExportFile file) =>
            JsonDocument.Parse(FunctionTransferCodec.Serialize(file)).RootElement.Clone();

        private static JsonElement AsElement(string json) => JsonDocument.Parse(json).RootElement.Clone();

        private static async Task<FunctionExportFile> ExportFromA(FunctionEntity? function = null) =>
            await SourceEnv(function).Service().ExportAsync(TenantA, FunctionId, "exporter", "e@x");

        /// <summary>Re-hashes after a test edits the body, so the test reaches the check it is about.</summary>
        private static JsonElement Rehashed(FunctionExportFile file)
        {
            file.ContentHash = FunctionTransferCodec.Hash(file.Function!);
            return AsElement(file);
        }

        // ============================================================== export ====

        [Fact]
        public async Task Export_leaves_out_every_environment_field()
        {
            var json = FunctionTransferCodec.Serialize(await ExportFromA());

            using var doc = JsonDocument.Parse(json);
            var function = doc.RootElement.GetProperty("function");
            function.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
                ["itemId", "name", "description", "source", "trigger", "outputActions", "variables"]);
            foreach (var forbidden in new[]
            {
                "status", "activeVersionId", "lastVersionNumber", "lastDeployedAt", "totalRuns", "lastRunAt", "deletion",
                "createdBy", "lastUpdatedBy", "organizationId", "imageDigest", "packages", "sourceHash", "createdDate",
                "lastUpdatedDate", "tags", "editor-a", "creator-a", "org-a", "ver-7",
            })
            {
                json.Should().NotContain($"\"{forbidden}\"", $"{forbidden} is environment-specific");
            }
            doc.RootElement.GetProperty("format").GetString().Should().Be("blocks.function.export");
            doc.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(2);
            function.GetProperty("itemId").GetString().Should().Be(FunctionId, "the same id in every environment");
            function.GetProperty("outputActions")[0].GetProperty("id").GetString().Should().Be("oa1", "output actions keep their ids");
        }

        [Fact]
        public async Task Export_writes_secret_ids_as_names_and_holds_no_id_or_value()
        {
            var file = await ExportFromA();
            var json = FunctionTransferCodec.Serialize(file);

            json.Should().NotContain("sec-a-1").And.NotContain(StripeValue).And.NotContain("{{secret.");
            file.Function!.Variables.Single(v => v.Key == "STRIPE_KEY").Value.Should().Be("{{secretName.stripe-key}}");
            file.Function.OutputActions[0].Headers["Authorization"].Should().Be("Bearer {{secretName.stripe-key}}");
            file.SecretNames.Should().ContainSingle(s => s.Name == "stripe-key")
                .Which.UsedBy.Should().BeEquivalentTo(["variable:STRIPE_KEY", "outputAction:oa1:header:Authorization"]);
            file.UnresolvedSecretRefs.Should().BeEmpty();
        }

        [Fact]
        public async Task Export_marks_an_id_that_has_no_secret_and_lists_it()
        {
            var file = await ExportFromA(StoredFunction(secretId: "sec-gone"));

            file.Function!.Variables.Single(v => v.Key == "STRIPE_KEY").Value.Should().Be(FunctionSecretRefs.UnresolvedMarker);
            file.UnresolvedSecretRefs.Should().BeEquivalentTo(["variable:STRIPE_KEY", "outputAction:oa1:header:Authorization"]);
            FunctionTransferCodec.Serialize(file).Should().NotContain("sec-gone");
        }

        [Fact]
        public async Task Export_refuses_when_the_catalog_cannot_be_read_rather_than_writing_ids()
        {
            var env = SourceEnv();
            env.Catalog.Down = true;

            var act = () => env.Service().ExportAsync(TenantA, FunctionId, "u", "e");

            await act.Should().ThrowAsync<FunctionUnavailableException>();
        }

        [Fact]
        public async Task Export_records_an_audit_row_with_the_hash_only()
        {
            var env = SourceEnv();
            var file = await env.Service().ExportAsync(TenantA, FunctionId, "u", "e");

            env.AuditRows.Should().ContainSingle(r => r.Action == FunctionsConstants.AuditActions.Exported)
                .Which.Detail.Should().Contain(file.ContentHash).And.NotContain("export default");
        }

        // ================================================================ hash ====

        [Fact]
        public async Task The_hash_is_stable_for_the_same_content_and_ignores_header_order()
        {
            var a = (await ExportFromA()).Function!;
            var b = FunctionTransferCodec.Copy(a);
            b.OutputActions[0].Headers = b.OutputActions[0].Headers.Reverse().ToDictionary(kv => kv.Key, kv => kv.Value);

            FunctionTransferCodec.Hash(a).Should().Be(FunctionTransferCodec.Hash(b));
            FunctionTransferCodec.Hash(a).Should().MatchRegex("^[0-9a-f]{64}$");

            b.Source.IndexJs += " ";
            FunctionTransferCodec.Hash(a).Should().NotBe(FunctionTransferCodec.Hash(b));
        }

        [Fact]
        public async Task The_hash_survives_a_download_and_reupload()
        {
            var file = await ExportFromA();
            var reread = FunctionTransferCodec.Parse(AsElement(file));

            FunctionTransferCodec.Hash(reread.Function!).Should().Be(file.ContentHash);
        }

        // ============================================================= preview ====

        [Fact]
        public async Task Preview_of_an_unknown_id_is_new_and_writes_nothing()
        {
            var target = new Env();
            target.Catalog.NameById["sec-b-9"] = "stripe-key";

            var preview = await target.Service().PreviewAsync(TenantB, new() { File = AsElement(await ExportFromA()) });

            preview.Status.Should().Be(FunctionImportStatuses.New);
            preview.FunctionId.Should().Be(FunctionId);
            preview.MissingSecrets.Should().BeEmpty();
            target.Store.Inserts.Should().Be(0);
            target.Store.Updates.Should().Be(0);
            target.Deploy.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Preview_lists_missing_and_duplicate_secret_names_and_install_scripts()
        {
            var target = new Env();
            var file = await ExportFromA(StoredFunction(allowScripts: true));
            file.Function!.Variables.Add(new VariableBinding { Key = "DB_PASSWORD", Value = "{{secretName.db-pass}}" });
            target.Catalog.NameById["d1"] = "db-pass";
            target.Catalog.NameById["d2"] = "db-pass";

            var preview = await target.Service().PreviewAsync(TenantB, new() { File = Rehashed(file) });

            preview.MissingSecrets.Should().BeEquivalentTo(["stripe-key"]);
            preview.DuplicateSecrets.Should().BeEquivalentTo(["db-pass"]);
            preview.AllowInstallScriptsRequested.Should().BeTrue();
            preview.Warnings.Should().Contain(w => w.Contains("install scripts", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Preview_of_the_same_content_is_same_even_when_the_secret_is_bound_by_id_here()
        {
            var file = await ExportFromA();
            var target = new Env();
            target.Catalog.NameById["sec-b-9"] = "stripe-key";
            target.Store.Live[FunctionId] = StoredFunction(secretId: "sec-b-9");

            var preview = await target.Service().PreviewAsync(TenantB, new() { File = AsElement(file) });

            preview.Status.Should().Be(FunctionImportStatuses.Same);
            preview.Fields.Should().BeEmpty();
            preview.Files.Should().BeEmpty();
        }

        [Fact]
        public async Task Preview_of_changed_content_returns_field_and_file_diffs_with_names_not_ids()
        {
            var file = await ExportFromA();
            file.Function!.Source.IndexJs = "export default async () => ({ ok: 2 });";
            file.Function.Trigger.AuthMode = AuthMode.Token;
            var target = new Env();
            target.Catalog.NameById["sec-b-9"] = "stripe-key";
            var mine = StoredFunction(secretId: "sec-b-9");
            mine.Variables.Add(new VariableBinding { Key = "EXTRA", Value = "1" });
            target.Store.Live[FunctionId] = mine;

            var preview = await target.Service().PreviewAsync(TenantB, new() { File = Rehashed(file) });

            preview.Status.Should().Be(FunctionImportStatuses.Changed);
            preview.TargetLastUpdatedDate.Should().Be(mine.LastUpdatedDate);
            preview.Fields.Select(f => f.Field).Should().BeEquivalentTo(["trigger", "variables"]);
            preview.Files.Should().ContainSingle(f => f.Field == "indexJs")
                .Which.Should().Match<FunctionImportFileDiffDto>(f => f.Mine!.Contains("ok: true") && f.Incoming!.Contains("ok: 2"));
            JsonSerializer.Serialize(preview).Should().NotContain("sec-b-9", "a preview never shows a secret id");
        }

        [Fact]
        public async Task Preview_of_a_tombstoned_id_is_blocked_with_a_reason()
        {
            var target = new Env();
            target.Store.Tombstoned[FunctionId] = StoredFunction();

            var preview = await target.Service().PreviewAsync(TenantB, new() { File = AsElement(await ExportFromA()) });

            preview.Status.Should().Be(FunctionImportStatuses.Blocked);
            preview.BlockedReason.Should().Contain("deleted");
        }

        [Fact]
        public async Task Preview_still_works_when_the_secret_store_is_down()
        {
            var target = new Env();
            target.Catalog.Down = true;

            var preview = await target.Service().PreviewAsync(TenantB, new() { File = AsElement(await ExportFromA()) });

            preview.Status.Should().Be(FunctionImportStatuses.New);
            preview.SecretsUnchecked.Should().BeTrue();
        }

        // ======================================================= refused files ====

        public static IEnumerable<object[]> BadFiles()
        {
            yield return ["format", (Action<FunctionExportFile>)(f => f.Format = "something.else"), "not a function export"];
            yield return ["schema", (Action<FunctionExportFile>)(f => f.SchemaVersion = 3), "schema version 3"];
            yield return ["schemaOld", (Action<FunctionExportFile>)(f => f.SchemaVersion = 1), "older version (schema 1)"];
            yield return ["itemId", (Action<FunctionExportFile>)(f => f.Function!.ItemId = "../../etc"), "not a valid function id"];
            yield return ["itemIdUpper", (Action<FunctionExportFile>)(f => f.Function!.ItemId = FunctionId.ToUpperInvariant()), "not a valid function id"];
        }

        [Theory]
        [MemberData(nameof(BadFiles))]
        public async Task A_malformed_file_is_refused(string _, Action<FunctionExportFile> spoil, string message)
        {
            var file = await ExportFromA();
            spoil(file);

            var act = () => new Env().Service().PreviewAsync(TenantB, new() { File = Rehashed(file) });

            (await act.Should().ThrowAsync<FunctionValidationException>()).Which.Message.Should().Contain(message);
        }

        [Fact]
        public async Task A_file_edited_after_export_is_refused_by_its_hash()
        {
            var file = await ExportFromA();
            file.Function!.Source.IndexJs = "export default async () => 'tampered';";

            var act = () => new Env().Service().PreviewAsync(TenantB, new() { File = AsElement(file) });

            (await act.Should().ThrowAsync<FunctionValidationException>()).Which.Message.Should().Contain("contentHash");
        }

        [Fact]
        public async Task A_raw_secret_id_in_the_file_is_refused()
        {
            var file = await ExportFromA();
            file.Function!.Variables[0].Value = "{{secret.sec-a-1}}";

            var act = () => new Env().Service().PreviewAsync(TenantB, new() { File = Rehashed(file) });

            (await act.Should().ThrowAsync<FunctionValidationException>()).Which.Message.Should().Contain("secret ids");
        }

        [Fact]
        public async Task A_typed_credential_in_the_file_is_refused_without_echoing_it()
        {
            var file = await ExportFromA();
            file.Function!.Variables[0].Value = StripeValue;

            var act = () => new Env().Service().ApplyAsync(TenantB, new() { File = Rehashed(file) }, Importer, "i@x");

            var thrown = await act.Should().ThrowAsync<FunctionValidationException>();
            thrown.Which.Message.Should().Contain("STRIPE_KEY").And.NotContain(StripeValue);
        }

        [Fact]
        public async Task An_oversized_file_is_refused()
        {
            var file = await ExportFromA();
            file.Function!.Source.IndexJs = "// " + new string('x', FunctionTransferCodec.MaxImportBytes);

            var act = () => new Env().Service().PreviewAsync(TenantB, new() { File = Rehashed(file) });

            (await act.Should().ThrowAsync<FunctionValidationException>()).Which.Message.Should().Contain("larger than");
        }

        [Fact]
        public async Task Source_over_the_function_limit_is_refused_by_the_normal_validation()
        {
            var file = await ExportFromA();
            file.Function!.Source.IndexJs = "// " + new string('x', 2 * 1024 * 1024 + 10);

            var act = () => new Env().Service().PreviewAsync(TenantB, new() { File = Rehashed(file) });

            (await act.Should().ThrowAsync<FunctionValidationException>()).Which.Message.Should().Contain("source limit");
        }

        [Fact]
        public async Task A_file_naming_too_many_secrets_is_refused()
        {
            var file = await ExportFromA();
            file.Function!.Variables = Enumerable.Range(0, 51)
                .Select(i => new VariableBinding { Key = $"V{i}", Value = $"{{{{secretName.s{i}}}}}" }).ToList();

            var act = () => new Env().Service().PreviewAsync(TenantB, new() { File = Rehashed(file) });

            (await act.Should().ThrowAsync<FunctionValidationException>()).Which.Message.Should().Contain("50");
        }

        [Fact]
        public async Task Something_that_is_not_an_object_is_refused()
        {
            var act = () => new Env().Service().PreviewAsync(TenantB, new() { File = AsElement("[1,2]") });
            await act.Should().ThrowAsync<FunctionValidationException>();
        }

        // =============================================================== apply ====

        [Fact]
        public async Task Apply_new_inserts_with_the_files_id_as_a_draft_with_install_scripts_off()
        {
            var target = new Env();
            target.Catalog.NameById["sec-b-9"] = "stripe-key";

            var result = await target.Service().ApplyAsync(
                TenantB, new() { File = AsElement(await ExportFromA(StoredFunction(allowScripts: true))) }, Importer, "i@x");

            result.Status.Should().Be("created");
            result.AllowInstallScriptsReset.Should().BeTrue();
            result.Messages.Should().Contain(m => m.Contains("install scripts", StringComparison.OrdinalIgnoreCase));
            var stored = target.Store.Live[FunctionId];
            stored.ItemId.Should().Be(FunctionId);
            stored.Status.Should().Be(FunctionStatus.Draft);
            stored.ActiveVersionId.Should().BeNull();
            stored.LastVersionNumber.Should().Be(0);
            stored.TotalRuns.Should().Be(0);
            stored.Source.AllowInstallScripts.Should().BeFalse();
            stored.CreatedBy.Should().Be(Importer);
            stored.OrganizationId.Should().Be("default", "never the source environment's organization");
            stored.CreatedDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
            stored.SourceHash.Should().Be(FunctionHashing.SourceHash(stored.Source));
            stored.Variables.Single(v => v.Key == "STRIPE_KEY").Value.Should().Be("{{secret.sec-b-9}}", "a name with one match is bound to this environment's id");
            stored.OutputActions[0].Headers["Authorization"].Should().Be("Bearer {{secret.sec-b-9}}");
        }

        [Fact]
        public async Task Apply_new_keeps_missing_and_duplicate_names_unbound_and_says_so()
        {
            var target = new Env();
            var file = await ExportFromA();
            file.Function!.Variables.Add(new VariableBinding { Key = "DB_PASSWORD", Value = "{{secretName.db-pass}}" });
            target.Catalog.NameById["d1"] = "db-pass";
            target.Catalog.NameById["d2"] = "db-pass";

            var result = await target.Service().ApplyAsync(TenantB, new() { File = Rehashed(file) }, Importer, "i@x");

            result.MissingSecrets.Should().BeEquivalentTo(["stripe-key"]);
            result.DuplicateSecrets.Should().BeEquivalentTo(["db-pass"]);
            var stored = target.Store.Live[FunctionId];
            stored.Variables.Single(v => v.Key == "STRIPE_KEY").Value.Should().Be("{{secretName.stripe-key}}");
            stored.Variables.Single(v => v.Key == "DB_PASSWORD").Value.Should().Be("{{secretName.db-pass}}");
        }

        [Fact]
        public async Task Export_carries_only_the_rate_limit_never_the_fixed_limits_or_retry()
        {
            var stored = StoredFunction();
            stored.Limits = new FunctionLimits { CpuMillicores = 999, MemoryMb = 999, TimeoutSeconds = 999, Concurrency = 99, RequestsPerMinute = 50 };
            stored.Retry = new RetryPolicy { Attempts = 9 };

            var json = FunctionTransferCodec.Serialize(await ExportFromA(stored));

            json.Should().NotContain("cpuMillicores").And.NotContain("memoryMb").And.NotContain("concurrency");
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("function").EnumerateObject().Select(p => p.Name)
                .Should().NotContain(["limits", "retry"]);
            doc.RootElement.GetProperty("function").GetProperty("requestsPerMinute").GetInt32().Should().Be(50);
            FunctionTransferCodec.Serialize(await ExportFromA()).Should().NotContain("requestsPerMinute");
        }

        [Fact]
        public async Task Apply_new_stores_the_platform_profile_and_keeps_the_rate_limit()
        {
            var target = new Env();
            var file = await ExportFromA();
            file.Function!.RequestsPerMinute = 50;

            await target.Service().ApplyAsync(TenantB, new() { File = Rehashed(file) }, Importer, "i@x");

            var stored = target.Store.Live[FunctionId];
            stored.Limits.Should().BeEquivalentTo(new FunctionLimits { RequestsPerMinute = 50 }.Clamp());
            stored.Retry.Should().BeEquivalentTo(RetryPolicy.Fixed);
        }

        [Fact]
        public async Task Apply_new_starts_the_build_and_deploy_and_reports_it()
        {
            var target = new Env();

            var result = await target.Service().ApplyAsync(TenantB, new() { File = AsElement(await ExportFromA()) }, Importer, "i@x");

            result.Deploy!.State.Should().Be(FunctionImportDeployStates.Building);
            result.Deploy.BuildId.Should().Be("build-1");
            target.Deploy.Verify(d => d.DeployAsync(TenantB, It.Is<DeployFunctionRequestDto>(r => r.FunctionId == FunctionId), Importer, "i@x", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_failed_build_does_not_undo_the_import()
        {
            var target = new Env();
            target.Deploy.Setup(d => d.DeployAsync(It.IsAny<string>(), It.IsAny<DeployFunctionRequestDto>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FunctionBuildFailedException("the build failed: npm ERR", "build-9"));

            var result = await target.Service().ApplyAsync(TenantB, new() { File = AsElement(await ExportFromA()) }, Importer, "i@x");

            result.Status.Should().Be("created");
            result.Deploy!.State.Should().Be(FunctionImportDeployStates.Failed);
            result.Deploy.BuildId.Should().Be("build-9");
            target.Store.Live.Should().ContainKey(FunctionId);
        }

        [Fact]
        public async Task Apply_writes_an_audit_row_without_source_or_values()
        {
            var target = new Env();
            await target.Service().ApplyAsync(TenantB, new() { File = AsElement(await ExportFromA()) }, Importer, "i@x");

            var row = target.AuditRows.Should().ContainSingle(r => r.Action == FunctionsConstants.AuditActions.Imported).Subject;
            row.Detail.Should().Contain("created").And.NotContain("export default").And.NotContain("stripe-key");
        }

        [Fact]
        public async Task Replaying_the_same_file_is_a_no_op()
        {
            var target = new Env();
            var file = AsElement(await ExportFromA());

            await target.Service().ApplyAsync(TenantB, new() { File = file }, Importer, "i@x");
            var again = await target.Service().ApplyAsync(
                TenantB, new() { File = file, Resolution = "takeIncoming", ExpectedLastUpdatedDate = DateTime.UtcNow }, Importer, "i@x");

            again.Status.Should().Be("unchanged");
            again.Deploy.Should().BeNull();
            target.Store.Inserts.Should().Be(1);
            target.Store.Updates.Should().Be(0);
            target.Deploy.Verify(d => d.DeployAsync(It.IsAny<string>(), It.IsAny<DeployFunctionRequestDto>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Apply_takeIncoming_updates_the_existing_function_conditionally()
        {
            var file = await ExportFromA();
            file.Function!.Source.IndexJs = "export default async () => 2;";
            file.Function.Description = "new words";
            var target = new Env();
            var mine = StoredFunction(secretId: "sec-b-9");
            mine.Source.AllowInstallScripts = true;
            target.Catalog.NameById["sec-b-9"] = "stripe-key";
            target.Store.Live[FunctionId] = mine;

            var result = await target.Service().ApplyAsync(TenantB, new()
            {
                File = Rehashed(file), Resolution = "takeIncoming", ExpectedLastUpdatedDate = mine.LastUpdatedDate,
            }, Importer, "i@x");

            result.Status.Should().Be("updated");
            var stored = target.Store.Live[FunctionId];
            stored.Source.IndexJs.Should().Be("export default async () => 2;");
            stored.Description.Should().Be("new words");
            stored.Source.AllowInstallScripts.Should().BeTrue("an import never changes the owner's install-scripts choice");
            stored.Status.Should().Be(FunctionStatus.Live, "status and versions are not touched by an import");
            stored.ActiveVersionId.Should().Be("ver-7");
            stored.Variables.Single(v => v.Key == "STRIPE_KEY").Value.Should().Be("{{secret.sec-b-9}}");
            result.Deploy.Should().NotBeNull();
        }

        [Fact]
        public async Task Apply_refuses_with_409_when_the_function_changed_after_the_preview()
        {
            var file = await ExportFromA();
            file.Function!.Name = "renamed";
            var target = new Env();
            var mine = StoredFunction();
            target.Store.Live[FunctionId] = mine;

            var act = () => target.Service().ApplyAsync(TenantB, new()
            {
                File = Rehashed(file), Resolution = "takeIncoming", ExpectedLastUpdatedDate = mine.LastUpdatedDate.AddSeconds(-5),
            }, Importer, "i@x");

            await act.Should().ThrowAsync<FunctionConflictException>();
            target.Store.Live[FunctionId].Name.Should().Be("orders");
            target.Deploy.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Apply_to_an_existing_function_needs_the_previewed_date()
        {
            var file = await ExportFromA();
            file.Function!.Name = "renamed";
            var target = new Env();
            target.Store.Live[FunctionId] = StoredFunction();

            var act = () => target.Service().ApplyAsync(TenantB, new() { File = Rehashed(file), Resolution = "takeIncoming" }, Importer, "i@x");

            await act.Should().ThrowAsync<FunctionValidationException>();
        }

        [Fact]
        public async Task Apply_merge_takes_only_the_chosen_fields_and_files()
        {
            var file = await ExportFromA();
            file.Function!.Name = "renamed";
            file.Function.Source.IndexJs = "export default async () => 'theirs';";
            file.Function.Source.PackageJson = """{"type":"module","dependencies":{"zod":"3.0.0"}}""";
            file.Function.Trigger.AuthMode = AuthMode.Token;
            var target = new Env();
            var mine = StoredFunction();
            target.Catalog.NameById["sec-a-1"] = "stripe-key";
            target.Store.Live[FunctionId] = mine;

            var result = await target.Service().ApplyAsync(TenantB, new()
            {
                File = Rehashed(file),
                Resolution = "merge",
                FieldChoices = new() { ["indexJs"] = "incoming", ["trigger"] = "incoming", ["name"] = "mine" },
                ExpectedLastUpdatedDate = mine.LastUpdatedDate,
            }, Importer, "i@x");

            result.Status.Should().Be("updated");
            var stored = target.Store.Live[FunctionId];
            stored.Source.IndexJs.Should().Contain("theirs");
            stored.Trigger.AuthMode.Should().Be(AuthMode.Token);
            stored.Name.Should().Be("orders", "name was kept");
            stored.Source.PackageJson.Should().Be("""{"type":"module"}""", "a file left out keeps mine");
            target.AuditRows.Single(r => r.Action == FunctionsConstants.AuditActions.Imported).Detail
                .Should().Contain("indexJs").And.Contain("trigger").And.NotContain("packageJson");
        }

        [Fact]
        public async Task Apply_keepMine_changes_nothing()
        {
            var file = await ExportFromA();
            file.Function!.Name = "renamed";
            var target = new Env();
            var mine = StoredFunction();
            target.Store.Live[FunctionId] = mine;

            var result = await target.Service().ApplyAsync(TenantB, new()
            {
                File = Rehashed(file), Resolution = "keepMine", ExpectedLastUpdatedDate = mine.LastUpdatedDate,
            }, Importer, "i@x");

            result.Status.Should().Be("unchanged");
            target.Store.Updates.Should().Be(0);
        }

        [Fact]
        public async Task Apply_refuses_an_unknown_resolution_or_field()
        {
            var file = await ExportFromA();
            file.Function!.Name = "renamed";
            var target = new Env();
            target.Store.Live[FunctionId] = StoredFunction();

            var bad = () => target.Service().ApplyAsync(TenantB, new() { File = Rehashed(file), Resolution = "overwrite" }, Importer, "i@x");
            await bad.Should().ThrowAsync<FunctionValidationException>();

            var badField = () => target.Service().ApplyAsync(TenantB, new()
            {
                File = Rehashed(file), Resolution = "merge", FieldChoices = new() { ["status"] = "incoming" },
            }, Importer, "i@x");
            await badField.Should().ThrowAsync<FunctionValidationException>();
        }

        [Fact]
        public async Task Apply_to_a_tombstoned_id_is_refused_with_409()
        {
            var target = new Env();
            target.Store.Tombstoned[FunctionId] = StoredFunction();

            var file = AsElement(await ExportFromA());
            var act = () => target.Service().ApplyAsync(TenantB, new() { File = file }, Importer, "i@x");

            await act.Should().ThrowAsync<FunctionConflictException>();
            target.Store.Inserts.Should().Be(0);
        }

        [Fact]
        public async Task A_hidden_existing_function_previews_as_new_but_is_never_overwritten()
        {
            var target = new Env();
            target.Store.Hidden.Add(FunctionId);
            var file = AsElement(await ExportFromA());

            (await target.Service().PreviewAsync(TenantB, new() { File = file })).Status.Should().Be(FunctionImportStatuses.New);
            var act = () => target.Service().ApplyAsync(TenantB, new() { File = file }, Importer, "i@x");

            await act.Should().ThrowAsync<FunctionConflictException>();
            target.Deploy.VerifyNoOtherCalls();
        }

        // ========================================================== round trip ====

        [Fact]
        public async Task Export_import_into_an_empty_store_and_export_again_is_identical_except_exportedAt()
        {
            var first = await ExportFromA();

            var target = new Env();
            target.Catalog.NameById["sec-b-9"] = "stripe-key";
            await target.Service().ApplyAsync(TenantB, new() { File = AsElement(first) }, Importer, "i@x");
            var second = await target.Service().ExportAsync(TenantB, FunctionId, "u", "e");

            second.ExportedAt = first.ExportedAt;
            FunctionTransferCodec.Serialize(second).Should().Be(FunctionTransferCodec.Serialize(first));
            second.ContentHash.Should().Be(first.ContentHash);
        }

        // ======================================================== permissions ====

        [Theory]
        [InlineData(nameof(FunctionsController.Export))]
        [InlineData(nameof(FunctionsController.ImportPreview))]
        [InlineData(nameof(FunctionsController.Import))]
        public void Every_transfer_endpoint_needs_manage(string action)
        {
            var method = typeof(FunctionsController).GetMethod(action)!;
            method.GetCustomAttribute<ProtectedEndPointAttribute>()!.ResourceName.Should().Be("blocks-logic::function::manage");
        }

        [Theory]
        [InlineData(nameof(FunctionsController.ImportPreview))]
        [InlineData(nameof(FunctionsController.Import))]
        public void Import_endpoints_cap_the_request_body(string action)
        {
            var limit = typeof(FunctionsController).GetMethod(action)!.GetCustomAttribute<RequestSizeLimitAttribute>();
            limit.Should().NotBeNull();
        }

        [Fact]
        public void Export_is_a_GET_and_imports_are_POSTs()
        {
            typeof(FunctionsController).GetMethod(nameof(FunctionsController.Export))!.GetCustomAttribute<HttpGetAttribute>().Should().NotBeNull();
            typeof(FunctionsController).GetMethod(nameof(FunctionsController.Import))!.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
            typeof(FunctionsController).GetMethod(nameof(FunctionsController.ImportPreview))!.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
        }
    }
}
