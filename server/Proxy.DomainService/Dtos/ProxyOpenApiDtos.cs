namespace Proxy.DomainService.Dtos
{
    /// <summary>What an OpenAPI document would produce, for the caller to review before anything is saved.</summary>
    public sealed class ProxyOpenApiPreviewDto
    {
        /// <summary>The document's first server URL, or empty when it declares none.</summary>
        public string BaseUrl { get; set; } = string.Empty;

        public List<ProxyOpenApiOperationDto> Operations { get; set; } = new();

        /// <summary>Reasons nothing can be imported. A non-empty list means the preview failed.</summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>Things worth knowing that did not stop the import — a skipped verb, a parse quibble.</summary>
        public List<string> Warnings { get; set; } = new();
    }

    public sealed class ProxyOpenApiOperationDto
    {
        /// <summary>The document's own operationId where it has one, else "METHOD path".</summary>
        public string OperationId { get; set; } = string.Empty;

        public string Method { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public List<string> QueryParameters { get; set; } = new();

        public List<string> HeaderParameters { get; set; } = new();

        /// <summary>Header names the document's security schemes imply. Names only, never values.</summary>
        public List<string> SecurityHeaders { get; set; } = new();

        /// <summary>The proxy already has a route for this method and path; importing would collide.</summary>
        public bool AlreadyExists { get; set; }
    }

    /// <summary>A specification to preview: either pasted, or at a URL for the server to fetch.</summary>
    public sealed class ProxyOpenApiPreviewRequestDto
    {
        public string? SpecJson { get; set; }

        /// <summary>
        /// Fetched through the same upstream guard as any other destination — a URL a caller chose is a
        /// request this server would otherwise make anywhere, including at cloud metadata.
        /// </summary>
        public string? SpecUrl { get; set; }

        /// <summary>Optional: the proxy whose existing routes should be checked for collisions.</summary>
        public string? ProxyId { get; set; }
    }
}
