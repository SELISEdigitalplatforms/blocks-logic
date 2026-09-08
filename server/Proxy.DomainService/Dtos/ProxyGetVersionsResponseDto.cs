using System.Text.Json.Serialization;
using Blocks.Genesis;

namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// Response of <c>POST /api/Proxy/GetVersions</c>, newest version first. Works even after the proxy is
    /// deleted. When the proxy never existed for the tenant, <see cref="HttpStatus"/> is 404 and
    /// <see cref="Code"/> is <c>PROXY_NOT_FOUND</c>.
    /// </summary>
    public sealed class ProxyGetVersionsResponseDto : BaseQueryListResponse<List<ProxyVersionDto>>
    {
        /// <summary>Stable failure code; <c>null</c> on success.</summary>
        public string? Code { get; set; }

        /// <summary>Human-readable message when the proxy is unknown.</summary>
        public string? Message { get; set; }

        /// <summary>HTTP status the controller should return (200 normally, 404 for an unknown proxy).</summary>
        [JsonIgnore]
        public int HttpStatus { get; set; } = 200;
    }

    /// <summary>One row of the console's <em>Change history</em> tab.</summary>
    public sealed class ProxyVersionDto
    {
        public string ItemId { get; set; } = string.Empty;

        public int VersionNumber { get; set; }

        /// <summary>"Create" | "ConfigUpdate" | "Toggle" | "Revert" | "Delete".</summary>
        public string Kind { get; set; } = string.Empty;

        public string ChangeSummary { get; set; } = string.Empty;

        /// <summary>
        /// Per-field edits for this row, with raw (unmasked) before / after values. Empty for Create / Delete rows.
        /// </summary>
        public List<ProxyFieldChangeDto> Changes { get; set; } = new();

        /// <summary>Who made the change (the version row's <c>CreatedBy</c>).</summary>
        public string? Who { get; set; }

        /// <summary>When the change was made (the version row's <c>CreatedDate</c>, UTC).</summary>
        public DateTime WhenUtc { get; set; }

        /// <summary><c>"v&lt;versionNumber&gt;"</c>.</summary>
        public string VersionLabel { get; set; } = string.Empty;
    }

    /// <summary>One field's <c>- before</c> / <c>+ after</c> pair in a history row. Values are raw (unmasked).</summary>
    public sealed class ProxyFieldChangeDto
    {
        /// <summary>Stable field address: <c>"name" | "upstream" | "enabled" | "methods" | "header:&lt;key&gt;" | "query:&lt;key&gt;"</c>.</summary>
        public string Field { get; set; } = string.Empty;

        /// <summary>Display label, e.g. <c>"methods"</c>, <c>"header Authorization"</c>.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Raw previous value; <c>null</c> ⇒ the field / row did not exist before.</summary>
        public string? Before { get; set; }

        /// <summary>Raw resulting value; <c>null</c> ⇒ the field / row was removed.</summary>
        public string? After { get; set; }
    }
}
