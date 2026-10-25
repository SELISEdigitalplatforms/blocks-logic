import { useState } from "react";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { Card } from "@/components/ui-kits/card/card";
import { showErrorToast } from "@/hooks/use-toast";
import {
  FUNCTION_BASE_URL_PLACEHOLDER,
  acceptedHttpVerbs,
  getFunctionBaseUrl,
  getFunctionClientPath,
  getFunctionPollPath,
  snippetBlocksKey,
} from "../../constants/endpoint.constant";
import { ITriggerConfig } from "../../types/function.types";

type InvokeSnippetCardProps = {
  functionId: string;
  trigger: ITriggerConfig;
};

type CallMode = "http" | "sse";

const MODES: { id: CallMode; label: string }[] = [
  { id: "http", label: "HTTP" },
  { id: "sse", label: "SSE" },
];

const LABEL_CLASS = "text-xs font-medium uppercase tracking-wide text-medium-emphasis";
const NOTE_CLASS = "text-xs leading-relaxed text-medium-emphasis";

const SSE_RESPONSE = [
  "200 OK · text/event-stream",
  "event: run",
  'data: { "runId": "run_7f21c4" }',
  "",
  "event: done",
  'data: { "runId": "run_7f21c4", "status": "SUCCEEDED",',
  '        "result": { "ok": true } }',
].join("\n");

/** Copy button with its own "Copied" state. */
const CopyButton = ({ text, label = "Copy" }: { text: string; label?: string }) => {
  const [copied, setCopied] = useState(false);

  // The clipboard API rejects without a secure context or permission; say so instead of
  // showing "Copied" for text that never reached the clipboard.
  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      setTimeout(() => setCopied(false), 1400);
    } catch {
      showErrorToast({ title: "Could not copy", errors: ["Select the text and copy it by hand."] });
    }
  };

  return (
    <button
      type="button"
      className="text-xs font-semibold text-primary hover:underline"
      onClick={() => void handleCopy()}
    >
      {copied ? "Copied ✓" : label}
    </button>
  );
};

const ModeToggle = ({ mode, onChange }: { mode: CallMode; onChange: (mode: CallMode) => void }) => (
  <div className="flex items-center gap-3">
    {MODES.map((item) => (
      <button
        key={item.id}
        type="button"
        aria-pressed={mode === item.id}
        className={`text-xs font-semibold hover:underline ${
          mode === item.id ? "text-primary underline" : "text-medium-emphasis"
        }`}
        onClick={() => onChange(item.id)}
      >
        {item.label}
      </button>
    ))}
  </div>
);

const StreamResponse = () => {
  return (
    <>
      <pre className="font-mono text-xs leading-relaxed">{SSE_RESPONSE}</pre>
      <span className={NOTE_CLASS}>
        Two events. <code className="font-mono">run</code> comes at once with the run id.{" "}
        <code className="font-mono">done</code> comes when the run ends, with the full response:
        what the handler returned, or every piece joined if it yields them. If the run fails you
        get <code className="font-mono">event: error</code> with{" "}
        <code className="font-mono">{"{ runId, status, errorCode, message }"}</code>. If it is
        still running when the wait ends, the error is{" "}
        <code className="font-mono">STILL_RUNNING</code> with a{" "}
        <code className="font-mono">pollToken</code>: poll for the result below.
      </span>
    </>
  );
};

const HttpResponse = ({ isSync }: { isSync: boolean }) => {
  if (!isSync) {
    return (
      <pre className="font-mono text-xs leading-relaxed">
        {'202 Accepted\n{ "runId": "run_7f21c4", "status": "QUEUED" }'}
      </pre>
    );
  }
  return (
    <>
      <pre className="font-mono text-xs leading-relaxed">
        {'200 OK\n{ "ok": true }   ← what the handler returned'}
      </pre>
      <span className={NOTE_CLASS}>
        The caller waits for the answer. Return{" "}
        <code className="font-mono">{"{ statusCode, headers, body }"}</code> to set the status,
        headers and body yourself; anything else is sent as JSON with 200. If the run takes too
        long, the call returns <code className="font-mono">202</code> with a{" "}
        <code className="font-mono">pollToken</code> instead.
      </span>
    </>
  );
};

const pollNote = (isSse: boolean, isSync: boolean) => {
  if (isSse) {
    return "Only needed when the last event is STILL_RUNNING: use its runId and pollToken. Poll until status is final.";
  }
  const base = "Use the runId and pollToken from the 202 answer. Poll until status is final.";
  return isSync ? `${base} Only needed after a 202.` : base;
};

/**
 * "Call it": a copyable curl for the public route and what the handler sees for it. The example
 * deliberately uses a sub-path and a query so the request shape is visible, not just a body. A
 * toggle switches the same call between a plain HTTP request and an SSE stream.
 */
export const InvokeSnippetCard = ({ functionId, trigger }: InvokeSnippetCardProps) => {
  const [mode, setMode] = useState<CallMode>("http");
  const project = useProjectStore().selectedProject;
  const realBaseUrl = getFunctionBaseUrl(project);
  const baseUrl = realBaseUrl ?? FUNCTION_BASE_URL_PLACEHOLDER;
  const blocksKey = snippetBlocksKey(project?.tenantId);
  // The trigger decides: an SSE function always answers in events, an async one never does, and
  // a sync one answers in events only when the caller asks (Accept: text/event-stream).
  const isSync = trigger.responseMode === "sync";
  const isSseTrigger = trigger.responseMode === "sse";
  const isSse = isSseTrigger || (isSync && mode === "sse");
  // x-blocks-key is not optional: it is how the platform resolves the tenant for anything under
  // the gateway, so a caller without it never reaches the function. It identifies the project, it
  // does not authenticate a user — that is what the Authorization header below is for on a
  // token-protected trigger.
  const keyHeader = `  -H "x-blocks-key: ${blocksKey}"`;
  const acceptHeader = isSync && isSse ? ` \\\n  -H "Accept: text/event-stream"` : "";
  const authHeader =
    trigger.authMode === "Token" ? ` \\\n  -H "Authorization: Bearer $BLOCKS_TOKEN"` : "";
  // The first verb the trigger accepts. A GET carries its input in the query; any other verb in
  // the body. The example shows one the trigger actually answers, so what the handler receives
  // below is what this call produces.
  const verb = acceptedHttpVerbs(trigger)[0];
  // -N turns curl's output buffering off, so each event shows up as it is sent.
  const curlHead = `curl${isSse ? " -N" : ""}${verb === "GET" ? "" : ` -X ${verb}`}`;
  const target = `"$BASE_URL${getFunctionClientPath(functionId, "orders/42")}?notify=true"`;
  const bodyPart =
    verb === "GET" ? "" : ` \\\n  -H "Content-Type: application/json" \\\n  -d '{"qty":2}'`;
  const snippet = `BASE_URL="${baseUrl}"\n\n${curlHead} ${target} \\\n${keyHeader}${acceptHeader}${authHeader}${bodyPart}`;
  const pollSnippet = `curl "$BASE_URL${getFunctionPollPath("<runId>")}" \\\n${keyHeader} \\\n  -H "x-poll-token: <pollToken>"`;
  const receives =
    verb === "GET"
      ? '{ "method": "GET", "path": "orders/42",\n  "query": { "notify": "true" },\n  "headers": { "accept": "*/*", … },\n  "body": null }'
      : `{ "method": "${verb}", "path": "orders/42",\n  "query": { "notify": "true" },\n  "headers": { "content-type": "application/json", … },\n  "body": { "qty": 2 } }`;

  return (
    <Card className="overflow-hidden rounded-xl">
      <div className="flex items-center justify-between gap-3 border-b px-4 py-3">
        <span className="text-sm font-semibold">Call it</span>
        {isSync && <ModeToggle mode={mode} onChange={setMode} />}
      </div>
      <div className="flex flex-col gap-1 border-b px-4 py-3">
        <div className="flex items-center justify-between gap-3">
          <span className={LABEL_CLASS}>Base URL</span>
          <CopyButton text={baseUrl} />
        </div>
        <code className="break-all font-mono text-xs">{baseUrl}</code>
        {realBaseUrl ? null : (
          <span className={NOTE_CLASS}>Add a custom domain to the project to fill this in.</span>
        )}
      </div>
      <div className="flex items-center justify-between gap-3 border-b px-4 py-2">
        <span className={LABEL_CLASS}>Call it</span>
        <CopyButton text={snippet} />
      </div>
      <pre
        data-testid="invoke-snippet"
        className="overflow-x-auto bg-surface-app px-4 py-3.5 font-mono text-xs leading-relaxed"
      >
        {snippet}
      </pre>
      <div className="flex items-center justify-between gap-3 border-y px-4 py-2">
        <span className={LABEL_CLASS}>Poll for the result</span>
        <CopyButton text={pollSnippet} />
      </div>
      <pre
        data-testid="poll-snippet"
        className="overflow-x-auto bg-surface-app px-4 py-3.5 font-mono text-xs leading-relaxed"
      >
        {pollSnippet}
      </pre>
      <div className="px-4 pb-3 pt-2">
        <span className={NOTE_CLASS}>{pollNote(isSse, isSync)}</span>
      </div>
      <div className="flex flex-col gap-2 border-t px-4 py-3">
        <span className={LABEL_CLASS}>Your handler receives</span>
        <pre className="font-mono text-xs leading-relaxed">{receives}</pre>
      </div>
      <div className="flex flex-col gap-2 border-t px-4 py-3">
        <span className={LABEL_CLASS}>Response</span>
        {isSse ? <StreamResponse /> : <HttpResponse isSync={isSync} />}
        <span className={NOTE_CLASS}>
          Every trigger — HTTP, workflow or a test from the editor — is listed under Runs.
        </span>
      </div>
    </Card>
  );
};
