namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// Body of <c>PUT /api/Proxy/Update</c>. Replaces name / upstream / methods / headers / query only.
    /// <c>Slug</c> is immutable and not accepted; a <c>slug</c> field in the payload is ignored.
    /// <c>Enabled</c> is unchanged by Update (use Toggle).
    /// </summary>
    public sealed class ProxyUpdateRequestDto
    {
        public string? ItemId { get; set; }

        public string? Name { get; set; }

        public string? Upstream { get; set; }

        public List<string>? Methods { get; set; }

        public List<ProxyKeyValueInputDto>? Headers { get; set; }

        public List<ProxyKeyValueInputDto>? Query { get; set; }

        /// <summary>
        /// Fields merged into the top level of the client's JSON body on POST / PUT / PATCH forwards.
        /// Empty / omitted ⇒ the body is forwarded unchanged.
        /// </summary>
        public List<ProxyKeyValueInputDto>? BodyMerge { get; set; }

        /// <summary>Per-method overrides. Reserved for Phase D-feature; a non-empty value is rejected today.</summary>
        public List<ProxyMethodConfigInputDto>? MethodConfigs { get; set; }
    }
}
