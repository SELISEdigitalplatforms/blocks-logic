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
        /// When <c>true</c>, a Blocks-delegated bearer token is added to the Authorization header
        /// (best-effort; omitted when no delegation grant is available). A manually-set Authorization
        /// header always takes precedence over this.
        /// </summary>
        public bool UseBlocksAuthorization { get; set; } = false;

        public bool HaveBody { get; set; } = false;

        public string BodyContentType { get; set; } = "json";

        public string Body { get; set; } = string.Empty;
    }
}

