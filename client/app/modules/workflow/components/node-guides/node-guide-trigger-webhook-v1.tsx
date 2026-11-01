import { GuideContent } from "./node-guide-content";

export const NodeGuideTriggerWebhookV1 = () => (
  <GuideContent
    title="Webhook trigger"
    description="Use this trigger to start a workflow when another app sends a request to the webhook URL. The incoming body becomes the data passed to the next node."
    steps={[
      "Use the Webhook URL: Test while building the workflow, or Production after the workflow is published.",
      "Choose the Body Mode. JSON (legacy) accepts only JSON. Any content also accepts forms, multipart file uploads, text and binary bodies. Webhooks created before Body Mode existed stay on JSON (legacy).",
      "In JSON (legacy) mode, send a POST request with a JSON body and include the x-blocks-key header. A JSON object creates one workflow item, and a JSON array of objects creates one item per object. Other values (a plain string, number, or an array of them) are rejected. The header name is case-insensitive.",
      "In Any content mode, every item has the same shape: { body, files, contentType, bodyFormat, headers, query }. bodyFormat is json, form, multipart, text, binary or empty. Forms become an object of text values (a repeated field becomes a list), text stays a string, and a JSON array still creates one item per element.",
      "In Any content mode, file parts and binary bodies (for example a PDF) are stored in the Files Folder ID of the chosen Storage Configuration (blank means the storage root of the default configuration). Each file appears in files as { field, fileId, name, contentType, size }, so a later node such as Send Mail can use {{$json.files[0].fileId}}.",
      "List the request headers the workflow needs in Allowed Headers (for example x-signature). Authorization, Proxy-Authorization, Cookie, Set-Cookie and x-blocks-key are never copied.",
      "The Deprecated Webhook URL still works and embeds the x-blocks-key value in the path, so it does not need the header. Prefer the Webhook URL for new integrations.",
      "Choose the authentication level: None for open webhooks, Blocks Authentication for signed-in callers, or Blocks Authorization when roles or permissions must be checked.",
      "For Blocks Authorization, choose the organization, authorization mode, and any required roles or permissions.",
      "Choose whether the webhook should respond immediately or wait until the last node finishes.",
    ]}
    notes={[
      "The test URL only works while the workflow is listening for test executions. The production URL requires a published workflow.",
      "Immediate response queues the workflow and returns a status quickly. After Last Node Completion waits for the workflow result.",
      "When waiting for the last node, Response Data controls whether the webhook returns all output items, the first output item, or no body.",
      "Roles and permissions can require all selected values or any selected value, depending on the chosen condition.",
      "Any content limits: 5 MiB for requests without files; 50 MiB and at most 10 files for requests with files. Each file must also fit the storage configuration's maximum file size. Larger requests get 413, XML and unknown character sets get 415.",
      "Files are stored as the signed-in caller for Blocks Authentication and Blocks Authorization, and as the platform's webhook service account for open webhooks; that account needs permission to add files to the folder. If any file cannot be stored, the request fails and no file from it is kept.",
      "The caller's credentials are checked before the body is read, so an unauthorized request gets 401 whatever its body.",
    ]}
  />
);
