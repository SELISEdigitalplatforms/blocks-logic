import { useState } from "react";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { Card } from "@/components/ui-kits/card/card";
import { toHttpVerb } from "../../constants/endpoint.constant";
import { buildInvokeUrl } from "../endpoint-badge";
import { ITriggerConfig } from "../../types/function.types";

type InvokeSnippetCardProps = {
  functionId: string;
  trigger: ITriggerConfig;
};

/**
 * "Call it": a copyable curl for the public route and what the handler sees for it. The example
 * deliberately uses a sub-path and a query so the request shape is visible, not just a body.
 */
export const InvokeSnippetCard = ({ functionId, trigger }: InvokeSnippetCardProps) => {
  const [copied, setCopied] = useState(false);
  const project = useProjectStore().selectedProject;
  const url = buildInvokeUrl(functionId, project);
  // x-blocks-key is not optional: it is how the platform resolves the tenant for anything under
  // the gateway, so a caller without it never reaches the function. It identifies the project, it
  // does not authenticate a user — that is what the Authorization header below is for on a
  // token-protected trigger.
  const authHeader =
    trigger.authMode === "Token" ? ` \\\n  -H "Authorization: Bearer $BLOCKS_TOKEN"` : "";
  const verb = toHttpVerb(trigger.httpMethod);
  // A GET carries its input in the query; a POST in the body. The example shows the one the
  // trigger actually answers, so what the handler receives below is what this call produces.
  const snippet =
    verb === "GET"
      ? `curl "${url}/orders/42?notify=true" \\\n  -H "x-blocks-key: <your project key>"${authHeader}`
      : `curl -X POST "${url}/orders/42?notify=true" \\\n  -H "x-blocks-key: <your project key>"${authHeader} \\\n  -H "Content-Type: application/json" \\\n  -d '{"qty":2}'`;
  const receives =
    verb === "GET"
      ? '{ "method": "GET", "path": "orders/42",\n  "query": { "notify": "true" },\n  "headers": { "accept": "*/*", … },\n  "body": null }'
      : '{ "method": "POST", "path": "orders/42",\n  "query": { "notify": "true" },\n  "headers": { "content-type": "application/json", … },\n  "body": { "qty": 2 } }';

  const handleCopy = () => {
    navigator.clipboard.writeText(snippet);
    setCopied(true);
    setTimeout(() => setCopied(false), 1400);
  };

  return (
    <Card className="overflow-hidden rounded-xl">
      <div className="flex items-center justify-between gap-3 border-b px-4 py-3">
        <span className="text-sm font-semibold">Call it</span>
        <button
          type="button"
          className="text-xs font-semibold text-primary hover:underline"
          onClick={handleCopy}
        >
          {copied ? "Copied ✓" : "Copy"}
        </button>
      </div>
      <pre className="overflow-x-auto bg-surface-app px-4 py-3.5 font-mono text-xs leading-relaxed">
        {snippet}
      </pre>
      <div className="flex flex-col gap-2 border-t px-4 py-3">
        <span className="text-xs font-medium uppercase tracking-wide text-medium-emphasis">
          Your handler receives
        </span>
        <pre className="font-mono text-xs leading-relaxed">{receives}</pre>
      </div>
      <div className="flex flex-col gap-2 border-t px-4 py-3">
        <span className="text-xs font-medium uppercase tracking-wide text-medium-emphasis">
          Response
        </span>
        <pre className="font-mono text-xs leading-relaxed">
          {'202 Accepted\n{ "runId": "run_7f21c4", "status": "QUEUED" }'}
        </pre>
        <span className="text-xs leading-relaxed text-medium-emphasis">
          Add <code className="font-mono">?wait=true</code> to hold the call open for the result, or
          poll <code className="font-mono">GET …/fn/runs/{"{runId}"}</code>. Every trigger — HTTP,
          workflow or a test from the editor — is listed under Runs.
        </span>
      </div>
    </Card>
  );
};
