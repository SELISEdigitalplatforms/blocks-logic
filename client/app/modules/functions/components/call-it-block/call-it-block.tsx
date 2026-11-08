import { useState } from "react";
import { useProjectStore, useT, tStatic } from "@seliseblocks/genesis-os";
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
import { SegmentedControl } from "../trigger-tab/segmented-control";

const LOGIC_FUNCTIONS_COMPONENTS_MODULE = "logic.functions.components";

type CallItBlockProps = {
  functionId: string;
  trigger: ITriggerConfig;
};

type CallMode = "http" | "sse";

const MODES: { value: CallMode; label: string }[] = [
  {
    value: "http",
    get label() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "INVOKE_SNIPPET_CARD.NORMAL");
    },
  },
  {
    value: "sse",
    get label() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "INVOKE_SNIPPET_CARD.LIVE_UPDATES");
    },
  },
];

const LABEL_CLASS = "text-xs font-medium uppercase tracking-wide text-medium-emphasis";
const NOTE_CLASS = "text-xs leading-relaxed text-medium-emphasis";

const SSE_RESPONSE = [
  "200 OK · live updates",
  "event: run",
  'data: { "runId": "run_7f21c4" }',
  "",
  "event: done",
  'data: { "runId": "run_7f21c4", "status": "SUCCEEDED",',
  '        "result": { "ok": true } }',
].join("\n");

/** Copy button with its own "Copied" state. */
const CopyButton = ({ text, label }: { text: string; label?: string }) => {
  const { t } = useT(LOGIC_FUNCTIONS_COMPONENTS_MODULE);
  const [copied, setCopied] = useState(false);

  // The clipboard API rejects without a secure context or permission; say so instead of
  // showing "Copied" for text that never reached the clipboard.
  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      setTimeout(() => setCopied(false), 1400);
    } catch {
      showErrorToast({
        title: t("INVOKE_SNIPPET_CARD.COULD_NOT_COPY"),
        errors: [t("INVOKE_SNIPPET_CARD.SELECT_THE_TEXT_AND")],
      });
    }
  };

  return (
    <button
      type="button"
      className="text-xs font-semibold text-primary hover:underline"
      onClick={handleCopy}
    >
      {copied ? t("INVOKE_SNIPPET_CARD.COPIED") : (label ?? t("common:COPY"))}
    </button>
  );
};

const ASYNC_RESPONSE = '202 Accepted\n{ "runId": "run_7f21c4", "status": "QUEUED" }';
const SYNC_RESPONSE = '200 OK\n{ "ok": true }   ← what your function returned';

/** What the caller gets back, and one plain line about it, for the response mode in play. */
const callerResponse = (isSse: boolean, isSync: boolean) => {
  if (isSse) {
    return {
      text: SSE_RESPONSE,
      help: tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "INVOKE_SNIPPET_CARD.HELP_LIVE"),
    };
  }
  if (isSync) {
    return {
      text: SYNC_RESPONSE,
      help: tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "INVOKE_SNIPPET_CARD.HELP_RESULT"),
    };
  }
  return {
    text: ASYNC_RESPONSE,
    help: tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "INVOKE_SNIPPET_CARD.HELP_RUN_ID"),
  };
};

const pollNote = (isSse: boolean, isSync: boolean) => {
  if (isSse)
    return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "INVOKE_SNIPPET_CARD.POLL_HELP_LIVE");
  if (isSync)
    return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "INVOKE_SNIPPET_CARD.POLL_HELP_RESULT");
  return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "INVOKE_SNIPPET_CARD.POLL_HELP_RUN_ID");
};

/**
 * "Call it": a copyable curl for the public route and what the function sees for it. The example
 * deliberately uses a sub-path and a query so the request shape is visible, not just a body. A
 * toggle switches the same call between a normal request and live updates (an SSE stream).
 */
export const CallItBlock = ({ functionId, trigger }: CallItBlockProps) => {
  const { t } = useT(LOGIC_FUNCTIONS_COMPONENTS_MODULE);
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
  const streamFlag = isSse ? " -N" : "";
  const verbFlag = verb === "GET" ? "" : ` -X ${verb}`;
  const curlHead = `curl${streamFlag}${verbFlag}`;
  const target = `"$BASE_URL${getFunctionClientPath(functionId, "orders/42")}?notify=true"`;
  const bodyPart =
    verb === "GET" ? "" : ` \\\n  -H "Content-Type: application/json" \\\n  -d '{"qty":2}'`;
  const snippet = `BASE_URL="${baseUrl}"\n\n${curlHead} ${target} \\\n${keyHeader}${acceptHeader}${authHeader}${bodyPart}`;
  const pollSnippet = `curl "$BASE_URL${getFunctionPollPath("<runId>")}" \\\n${keyHeader} \\\n  -H "x-poll-token: <pollToken>"`;
  const receives =
    verb === "GET"
      ? '{ "method": "GET", "path": "orders/42",\n  "query": { "notify": "true" },\n  "headers": { "accept": "*/*", … },\n  "body": null }'
      : `{ "method": "${verb}", "path": "orders/42",\n  "query": { "notify": "true" },\n  "headers": { "content-type": "application/json", … },\n  "body": { "qty": 2 } }`;

  const response = callerResponse(isSse, isSync);

  return (
    <Card className="overflow-hidden rounded-xl">
      <div className="flex items-center justify-between gap-3 border-b px-4 py-3">
        <h3 className="text-sm font-semibold">{t("INVOKE_SNIPPET_CARD.CALL_IT")}</h3>
        {isSync && (
          <SegmentedControl
            value={mode}
            onChange={setMode}
            options={MODES}
            ariaLabel={t("INVOKE_SNIPPET_CARD.EXAMPLE_TYPE")}
          />
        )}
      </div>
      <div className="flex flex-col gap-1 border-b px-4 py-3">
        <div className="flex items-center justify-between gap-3">
          <span className={LABEL_CLASS}>{t("INVOKE_SNIPPET_CARD.YOUR_ADDRESS")}</span>
          <CopyButton text={baseUrl} />
        </div>
        <code className="break-all font-mono text-xs">{baseUrl}</code>
        {realBaseUrl ? null : (
          <span className={NOTE_CLASS}>{t("INVOKE_SNIPPET_CARD.ADD_A_CUSTOM_DOMAIN")}</span>
        )}
      </div>
      <div className="flex items-center justify-between gap-3 border-b px-4 py-2">
        <span className={LABEL_CLASS}>{t("INVOKE_SNIPPET_CARD.SEND_A_REQUEST")}</span>
        <CopyButton text={snippet} />
      </div>
      <pre
        data-testid="invoke-snippet"
        className="overflow-x-auto bg-surface-app px-4 py-3.5 font-mono text-xs leading-relaxed"
      >
        {snippet}
      </pre>
      <div className="px-4 pb-3 pt-2">
        <span className={NOTE_CLASS}>{t("INVOKE_SNIPPET_CARD.KEY_HEADER_HELP")}</span>
      </div>
      <div className="flex items-center justify-between gap-3 border-y px-4 py-2">
        <span className={LABEL_CLASS}>{t("INVOKE_SNIPPET_CARD.CHECK_THE_RESULT_LATER")}</span>
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
        <span className={LABEL_CLASS}>{t("INVOKE_SNIPPET_CARD.YOUR_FUNCTION_GETS")}</span>
        <pre className="font-mono text-xs leading-relaxed">{receives}</pre>
      </div>
      <div className="flex flex-col gap-2 border-t px-4 py-3">
        <span className={LABEL_CLASS}>{t("INVOKE_SNIPPET_CARD.THE_CALLER_GETS_BACK")}</span>
        <pre className="font-mono text-xs leading-relaxed">{response.text}</pre>
        <span className={NOTE_CLASS}>{response.help}</span>
      </div>
    </Card>
  );
};
