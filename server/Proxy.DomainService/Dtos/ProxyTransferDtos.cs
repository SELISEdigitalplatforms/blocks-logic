using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// One proxy as a file (export / import, 2026-10-18). The same file imported into another environment gives the
    /// same proxy: same <c>itemId</c>, same slug, same config; only the tenant differs. Secrets travel by name only
    /// (<c>{{$VAR.name}}</c>); a literal value under a credential-looking key is written as <c>***</c>.
    /// </summary>
    public sealed class ProxyExportFile
    {
        /// <summary>Always <c>"blocks.proxy.export"</c>.</summary>
        public string? Format { get; set; }

        public int? SchemaVersion { get; set; }

        /// <summary>Informational. Not part of <see cref="ContentHash"/>, so two exports of one proxy differ only here.</summary>
        public DateTime? ExportedAt { get; set; }

        /// <summary>The config, as a <see cref="ProxyExportConfig"/>. Kept raw so the hash covers exactly what was sent.</summary>
        public JsonObject? Proxy { get; set; }

        /// <summary>Every <c>{{$VAR.name}}</c> name the config uses. Informational; import recomputes it.</summary>
        public List<string>? SecretNames { get; set; }

        /// <summary>Addresses of values written as <c>***</c>, e.g. <c>header:Authorization</c>. Informational.</summary>
        public List<string>? MaskedValues { get; set; }

        /// <summary>sha256 (lower-case hex) of the canonical JSON of <see cref="Proxy"/>.</summary>
        public string? ContentHash { get; set; }
    }

    /// <summary>
    /// The behaviour-defining part of a proxy, in the same shapes Create / Update take, so an import runs through
    /// the same <c>ProxyConfigValidator</c>. No tenant, audit, version, stats or secret ids.
    /// </summary>
    public sealed class ProxyExportConfig
    {
        public string? ItemId { get; set; }

        public string? Slug { get; set; }

        public string? Name { get; set; }

        public string? Upstream { get; set; }

        public List<string>? Methods { get; set; }

        public bool Enabled { get; set; } = true;

        public ProxyAccessInputDto? Access { get; set; }

        public List<ProxyKeyValueInputDto>? Headers { get; set; }

        public List<ProxyKeyValueInputDto>? Query { get; set; }

        public List<ProxyKeyValueInputDto>? BodyMerge { get; set; }

        public List<ProxyMethodConfigInputDto>? MethodConfigs { get; set; }

        public List<ProxyRouteConfigInputDto>? Routes { get; set; }

        public string? ResponseMode { get; set; }

        public List<string>? ResponseInclude { get; set; }

        public ProxyResilienceInputDto? Resilience { get; set; }

        public int? RequestsPerMinute { get; set; }
    }

    /// <summary>Body of <c>POST /api/Proxies/import/apply</c>.</summary>
    public sealed class ProxyImportApplyRequestDto
    {
        public ProxyExportFile? File { get; set; }

        /// <summary><c>"takeIncoming"</c> | <c>"keepMine"</c> | <c>"merge"</c>. Only read when the proxy exists and differs.</summary>
        public string? Resolution { get; set; }

        /// <summary>For <c>merge</c>: field address &rarr; <c>"mine"</c> | <c>"incoming"</c>. A field not listed keeps mine.</summary>
        public Dictionary<string, string>? FieldChoices { get; set; }

        /// <summary>The <c>currentVersion</c> the preview showed. Moved on since ⇒ 409 <c>PROXY_VERSION_CONFLICT</c>.</summary>
        public int? ExpectedVersion { get; set; }
    }

    /// <summary>What an import would do. Same shape for preview and apply; apply also sets <see cref="Applied"/>.</summary>
    public sealed class ProxyImportResultDto
    {
        /// <summary><c>"new"</c> | <c>"same"</c> | <c>"changed"</c> | <c>"blocked"</c>.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Why an import is blocked, in plain words. <c>null</c> otherwise.</summary>
        public string? Reason { get; set; }

        /// <summary><c>"slugConflict"</c> | <c>"slugMismatch"</c> when blocked.</summary>
        public string? ReasonCode { get; set; }

        public string ItemId { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>The proxy's version in this environment (changed / same), or the version an insert writes (new).</summary>
        public int? CurrentVersion { get; set; }

        /// <summary>The id is not here but its change history is (it was deleted). The insert numbers versions after it.</summary>
        public bool HistoryExists { get; set; }

        /// <summary>Field-level differences, this environment ("mine") vs the file ("incoming"). Credentials shown as <c>***</c>.</summary>
        public List<ProxyImportFieldDto> Changes { get; set; } = new();

        /// <summary>Secret names with no secret of that exact name here. Import still works; calls fail until it is added.</summary>
        public List<string> MissingSecrets { get; set; } = new();

        /// <summary>Secret names that match more than one secret here. Left unlinked; a call uses the newest.</summary>
        public List<string> DuplicateSecrets { get; set; } = new();

        /// <summary><c>true</c> when Blocks Secrets could not be read, so the two lists above are unknown.</summary>
        public bool SecretCheckUnavailable { get; set; }

        /// <summary>Addresses of values the file carries as <c>***</c>. An existing value here is kept.</summary>
        public List<string> MaskedValues { get; set; } = new();

        /// <summary>This environment's config as canonical, indented JSON (credentials masked), for a side-by-side view.</summary>
        public string? MineJson { get; set; }

        /// <summary>The file's config as canonical, indented JSON.</summary>
        public string? IncomingJson { get; set; }

        /// <summary>Apply only: <c>true</c> when something was written.</summary>
        public bool Applied { get; set; }
    }

    public sealed class ProxyImportFieldDto
    {
        public string Field { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public string? Mine { get; set; }

        public string? Incoming { get; set; }
    }

    /// <summary>Service result for export / preview / apply: either data or a failure with a code.</summary>
    public sealed class ProxyTransferResponse<T> where T : class
    {
        public T? Data { get; set; }

        public string? Code { get; set; }

        public string? Message { get; set; }

        public IDictionary<string, string>? Errors { get; set; }

        [JsonIgnore]
        public int HttpStatus { get; set; } = 200;

        [JsonIgnore]
        public bool IsSuccess => HttpStatus is >= 200 and < 300;

        public static ProxyTransferResponse<T> Ok(T data) => new() { Data = data };

        public static ProxyTransferResponse<T> Fail(
            int httpStatus, string code, string message, IDictionary<string, string>? errors = null, T? data = null) => new()
        {
            HttpStatus = httpStatus,
            Code = code,
            Message = message,
            Errors = errors,
            Data = data,
        };
    }
}
