import { useState } from "react";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { Card } from "@/components/ui-kits/card/card";
import { acceptedHttpVerbs, snippetBlocksKey } from "../../constants/endpoint.constant";
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
  const blocksKey = snippetBlocksKey(project?.tenantId);
  // x-blocks-key is not optional: it is how the platform resolves the tenant for anything under
  // the gateway, so a caller without it never reaches the function. It identifies the project, it
  // does not authenticate a user — that is what the Authorization header below is for on a
  // token-protected trigger.
  const authHeader =
    trigger.authMode === "Token" ? ` \\\n  -H "Authorization: Bearer $BLOCKS_TOKEN"` : "";
  // The first verb the trigger accepts. A GET carries its input in the query; any other verb in
  // the body. The example shows one the trigger actually answers, so what the handler receives
  // below is what this call produces.
  const verb = acceptedHttpVerbs(trigger)[0];
  const isSync = trigger.responseMode === "sync";
  const snippet =
    verb === "GET"
      ? `curl "${url}/orders/42?notify=true" \\\n  -H "x-blocks-key: ${blocksKey}"${authHeader}`
      : `curl -X ${verb} "${url}/orders/42?notify=true" \\\n  -H "x-blocks-key: ${blocksKey}"${authHeader} \\\n  -H "Content-Type: application/json" \\\n  -d '{"qty":2}'`;
  const receives =
    verb === "GET"
      ? '{ "method": "GET", "path": "orders/42",\n  "query": { "notify": "true" },\n  "headers": { "accept": "*/*", … },\n  "body": null }'
      : `{ "method": "${verb}", "path": "orders/42",\n  "query": { "notify": "true" },\n  "headers": { "content-type": "application/json", … },\n  "body": { "qty": 2 } }`;

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
        {isSync ? (
          <>
            <pre className="font-mono text-xs leading-relaxed">
              {'200 OK\n{ "ok": true }   ← what the handler returned'}
            </pre>
            <span className="text-xs leading-relaxed text-medium-emphasis">
              The caller waits for the answer. Return{" "}
              <code className="font-mono">{"{ statusCode, headers, body }"}</code> to set the
              status, headers and body yourself; anything else is sent as JSON with 200. If the run
              takes too long, the call returns <code className="font-mono">202</code> with a{" "}
              <code className="font-mono">pollToken</code> instead.
            </span>
          </>
        ) : (
          <pre className="font-mono text-xs leading-relaxed">
            {'202 Accepted\n{ "runId": "run_7f21c4", "status": "QUEUED" }'}
          </pre>
        )}
        <span className="text-xs leading-relaxed text-medium-emphasis">
          {isSync
            ? "After a 202, collect the result with"
            : "The call always returns straight away. Collect the result with"}{" "}
          <code className="font-mono">GET …/fn/runs/{"{runId}"}</code>, using the{" "}
          <code className="font-mono">pollToken</code> that came back with it. Every trigger — HTTP,
          workflow or a test from the editor — is listed under Runs.
        </span>
      </div>
    </Card>
  );
};
