namespace DomainService.Workflow.Nodes.ActionHttpRequestV1
{
    public class ActionHttpRequestV1Parameters
    {
        public string HttpMethod { get; set; } = "GET";
        public string Url { get; set; } = string.Empty;

        public bool HaveQueryParameters { get; set; } = false;
        public Dictionary<string, string> QueryParameters { get; set; } = new Dictionary<string, string>();

        public bool HaveHeaders { get; set; } = false;
        public Dictionary<string, string> Headers { get; set; } = new();

        /// <summary>
        /// Authentication mode: <c>blocksAuthentication</c> (delegated token) or
        /// <c>clientCredential</c>. Empty means do not add an Authorization header.
        /// </summary>
        public string AuthenticationType { get; set; } = string.Empty;

        /// <summary>
        /// Client ID when <see cref="AuthenticationType"/> is <c>clientCredential</c>.
        /// </summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// Client secret when <see cref="AuthenticationType"/> is <c>clientCredential</c>.
        /// </summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// Legacy switch from saved workflows. Treated as <c>blocksAuthentication</c>
        /// when <see cref="AuthenticationType"/> is empty.
        /// </summary>
        public bool UseBlocksAuthorization { get; set; } = false;

        public bool HaveBody { get; set; } = false;

        public string BodyContentType { get; set; } = "json";

        public string Body { get; set; } = string.Empty;
    }
}

