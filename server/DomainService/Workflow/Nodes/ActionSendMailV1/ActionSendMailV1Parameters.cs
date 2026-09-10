using System.Text.Json;

namespace DomainService.Workflow.Nodes.ActionSendMailV1
{
    public class ActionSendMailV1Parameters
    {
        public string ProjectKey { get; set; } = string.Empty;
        public string Template { get; set; } = string.Empty;
        public string Language { get; set; } = "en-US";
        public string To { get; set; } = string.Empty;
        public Dictionary<string, string> BodyDataContext { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Storage File IDs to attach. Each entry is either a literal File ID, or an expression
        /// (e.g. "{{$json.fileId}}", "{{$node[\"generatePdf\"].json.output.fileId}}") resolved
        /// per-iteration the same way <see cref="To"/> is resolved. Missing key deserializes to
        /// an empty list (no migration needed for existing saved workflows).
        /// </summary>
        public List<string> Attachments { get; set; } = new List<string>();
    }
}
