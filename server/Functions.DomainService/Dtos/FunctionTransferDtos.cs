using System.Text.Json;
using Blocks.Genesis.Entities.Logic;

namespace Functions.DomainService.Dtos
{
    // ---------------------------------------------------------------- the file ----

    /// <summary>
    /// One function as an export file (<c>blocks.function.export</c>, schema 1). The same <see cref="FunctionExportBody.ItemId"/>
    /// in every environment, so <c>/api/fn/{id}</c> is the same URL everywhere; only the tenant differs.
    /// <para>
    /// Holds no secret value and no secret id: a <c>{{secret.&lt;id&gt;}}</c> is written as <c>{{secretName.&lt;name&gt;}}</c>
    /// (the name in the source environment), or as <c>{{secretUnresolved}}</c> when that id had no secret there.
    /// Nothing environment-specific either — no runs, logs, builds, artifacts, image digests, versions, status,
    /// counters, deletion state, authors, organization or access lists.
    /// </para>
    /// </summary>
    public sealed class FunctionExportFile
    {
        public string Format { get; set; } = string.Empty;
        public int SchemaVersion { get; set; }
        public DateTime ExportedAt { get; set; }
        public FunctionExportBody? Function { get; set; }

        /// <summary>The secret names the function uses and where (<c>variable:KEY</c>, <c>outputAction:{id}:header:{name}</c>…).</summary>
        public List<FunctionExportSecretName> SecretNames { get; set; } = [];

        /// <summary>References whose secret did not exist in the source environment (exported as <c>{{secretUnresolved}}</c>).</summary>
        public List<string> UnresolvedSecretRefs { get; set; } = [];

        /// <summary>sha256 (hex) over the canonical JSON of <see cref="Function"/> — <c>FunctionTransferCodec.Hash</c>.</summary>
        public string ContentHash { get; set; } = string.Empty;
    }

    public sealed class FunctionExportBody
    {
        public string ItemId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public FunctionExportSource Source { get; set; } = new();

        /// <summary>
        /// The function's own HTTP calls-per-minute limit (FN-19) — the one limit a tenant sets. Left out when unset.
        /// CPU, memory, timeout, concurrency and retry are fixed by the platform, so a file never carries them: an import
        /// always gets the platform profile (<see cref="FunctionLimits.Clamp"/>, <see cref="RetryPolicy.Fixed"/>).
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public int? RequestsPerMinute { get; set; }

        public TriggerConfig Trigger { get; set; } = new();
        public List<OutputAction> OutputActions { get; set; } = [];
        public List<VariableBinding> Variables { get; set; } = [];
    }

    public sealed class FunctionExportSource
    {
        public string IndexJs { get; set; } = string.Empty;
        public string PackageJson { get; set; } = string.Empty;
        public string? LockJson { get; set; }
        public bool AllowInstallScripts { get; set; }
    }

    public sealed class FunctionExportSecretName
    {
        public string Name { get; set; } = string.Empty;
        public List<string> UsedBy { get; set; } = [];
    }

    // -------------------------------------------------------------- requests ----

    public sealed class FunctionImportPreviewRequestDto
    {
        /// <summary>The export file exactly as downloaded (a JSON object).</summary>
        public JsonElement File { get; set; }
    }

    public sealed class FunctionImportApplyRequestDto
    {
        public JsonElement File { get; set; }

        /// <summary><c>takeIncoming</c>, <c>keepMine</c> or <c>merge</c>. Only read when the function already exists.</summary>
        public string? Resolution { get; set; }

        /// <summary>
        /// For <c>merge</c>: field → <c>mine</c> | <c>incoming</c>. Fields: name, description, trigger, rateLimit,
        /// variables, outputActions, indexJs, packageJson, lockJson. A field left out keeps mine.
        /// </summary>
        public Dictionary<string, string>? FieldChoices { get; set; }

        /// <summary>The <c>targetLastUpdatedDate</c> the preview showed. Required to change an existing function.</summary>
        public DateTime? ExpectedLastUpdatedDate { get; set; }
    }

    // ------------------------------------------------------------- responses ----

    public static class FunctionImportStatuses
    {
        public const string New = "new";
        public const string Same = "same";
        public const string Changed = "changed";
        public const string Blocked = "blocked";
    }

    public sealed class FunctionImportPreviewDto
    {
        /// <summary><c>new</c>, <c>same</c>, <c>changed</c> or <c>blocked</c>.</summary>
        public string Status { get; set; } = string.Empty;
        public string FunctionId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ContentHash { get; set; } = string.Empty;

        public string? TargetName { get; set; }

        /// <summary>Send back as <c>expectedLastUpdatedDate</c>; an apply refuses with 409 when it moved.</summary>
        public DateTime? TargetLastUpdatedDate { get; set; }

        public string? BlockedReason { get; set; }

        /// <summary>Changed settings only, both sides as display text (secret ids shown as names).</summary>
        public List<FunctionImportFieldDiffDto> Fields { get; set; } = [];

        /// <summary>Changed source files only, both sides in full, for a diff editor.</summary>
        public List<FunctionImportFileDiffDto> Files { get; set; } = [];

        /// <summary>Names in the file with no secret here: add a secret with that exact name and it works.</summary>
        public List<string> MissingSecrets { get; set; } = [];

        /// <summary>Names that match more than one secret here: they stay unbound until only one is left.</summary>
        public List<string> DuplicateSecrets { get; set; } = [];

        /// <summary>Where the file carries <c>{{secretUnresolved}}</c> (no secret existed where it was exported).</summary>
        public List<string> UnresolvedSecretRefs { get; set; } = [];

        /// <summary>The secret store could not be asked; missing / duplicate names are unknown.</summary>
        public bool SecretsUnchecked { get; set; }

        /// <summary>The file has install scripts on. An import never turns them on.</summary>
        public bool AllowInstallScriptsRequested { get; set; }

        public List<string> Warnings { get; set; } = [];
    }

    public sealed class FunctionImportFieldDiffDto
    {
        public string Field { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string? Mine { get; set; }
        public string? Incoming { get; set; }
    }

    public sealed class FunctionImportFileDiffDto
    {
        /// <summary>The apply field name: indexJs, packageJson or lockJson.</summary>
        public string Field { get; set; } = string.Empty;

        /// <summary>The file name shown: index.js, package.json, package-lock.json.</summary>
        public string File { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string? Mine { get; set; }
        public string? Incoming { get; set; }
    }

    public sealed class FunctionImportResultDto
    {
        /// <summary><c>created</c>, <c>updated</c> or <c>unchanged</c>.</summary>
        public string Status { get; set; } = string.Empty;
        public string FunctionId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        /// <summary>True when the file asked for install scripts: they arrive off; the owner can turn them on.</summary>
        public bool AllowInstallScriptsReset { get; set; }

        public List<string> MissingSecrets { get; set; } = [];
        public List<string> DuplicateSecrets { get; set; } = [];
        public List<string> Messages { get; set; } = [];

        /// <summary>The automatic build + deploy after a created / updated import; null when nothing changed.</summary>
        public FunctionImportDeployDto? Deploy { get; set; }
    }

    public static class FunctionImportDeployStates
    {
        public const string Live = "live";
        public const string Building = "building";
        public const string Failed = "failed";
    }

    public sealed class FunctionImportDeployDto
    {
        /// <summary><c>live</c>, <c>building</c> (watch <see cref="BuildId"/>, then deploy it) or <c>failed</c>.</summary>
        public string State { get; set; } = string.Empty;
        public string? BuildId { get; set; }
        public int? VersionNumber { get; set; }
        public string? Error { get; set; }
    }
}
