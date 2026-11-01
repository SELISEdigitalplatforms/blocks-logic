import { HttpTriggerVerb, ITriggerConfig, TriggerResponseMode } from "../types/function.types";
import { FUNCTION_HTTP_VERBS } from "../constants/endpoint.constant";
import { tStatic } from "@seliseblocks/genesis-os";

/** The values a trigger has for the reuse / response / verbs settings when the server sent none. */
export const TRIGGER_REUSE_DEFAULTS: Pick<
  ITriggerConfig,
  "httpMethods" | "reuseSandbox" | "responseMode"
> = {
  httpMethods: [],
  reuseSandbox: false,
  responseMode: "async",
};

/**
 * Fills the settings a function saved before they existed does not carry, so the editor always
 * holds a complete trigger. Applied on hydrate: the saved snapshot and the working copy then
 * match, and loading an old function does not mark it dirty.
 */
export const withTriggerDefaults = (trigger: ITriggerConfig): ITriggerConfig => {
  // A server from before the cookie option was removed may still send `allowSetCookie`; leave it
  // out so it never reaches the editor's snapshot or a save.
  const { allowSetCookie: _removed, ...rest } = trigger as ITriggerConfig & { allowSetCookie?: unknown };
  const partial = rest as Partial<ITriggerConfig>;
  const responseMode: TriggerResponseMode =
    partial.responseMode === "sync" || partial.responseMode === "sse" ? partial.responseMode : "async";
  const httpMethods = Array.isArray(partial.httpMethods)
    ? FUNCTION_HTTP_VERBS.filter((verb) =>
        (partial.httpMethods as string[]).some((m) => m.toUpperCase() === verb),
      )
    : TRIGGER_REUSE_DEFAULTS.httpMethods;
  return {
    ...(rest as ITriggerConfig),
    httpMethods: httpMethods as HttpTriggerVerb[],
    reuseSandbox: partial.reuseSandbox === true,
    responseMode,
  };
};

/** Leftover work, by the async resource name the sandbox reports, in plain words. */
const LEFTOVER_LABELS: Array<{ match: RegExp; label: string }> = [
  { match: /^(timeout|interval|immediate)$/i, get label() { return tStatic("logic.functions", "TRIGGER.A_TIMER"); } },
  { match: /^(httpclientrequest|httpincomingmessage|fetch)$/i, get label() { return tStatic("logic.functions", "TRIGGER.AN_HTTP_REQUEST"); } },
  { match: /^(fsreq|filehandle|statwatcher|fsevent)/i, get label() { return tStatic("logic.functions", "TRIGGER.A_FILE_OPERATION"); } },
  { match: /^(getaddrinfo|getnameinfo|querywrap)/i, get label() { return tStatic("logic.functions", "TRIGGER.A_DNS_LOOKUP"); } },
  { match: /^(tcpconnect|pipeconnect|shutdownwrap|writewrap)/i, get label() { return tStatic("logic.functions", "TRIGGER.A_NETWORK_CONNECTION"); } },
  { match: /^processwrap$/i, get label() { return tStatic("logic.functions", "TRIGGER.A_CHILD_PROCESS"); } },
  { match: /^worker$/i, get label() { return tStatic("logic.functions", "TRIGGER.A_WORKER_THREAD"); } },
  {
    match: /^waituntil$/i,
    get label() {
      return tStatic("logic.functions", "TRIGGER.WAITUNTIL_WORK_UNFINISHED");
    },
  },
];

const OTHER_DISCARD_REASONS: Record<string, string> = {
  get timeout() { return tStatic("logic.functions", "TRIGGER.THE_CALL_OR_WORK"); },
  get drain() { return tStatic("logic.functions", "TRIGGER.A_NEWER_VERSION_WENT"); },
  get cancelled() { return tStatic("logic.functions", "TRIGGER.THE_RUN_WAS_CANCELLED"); },
  get crash() { return tStatic("logic.functions", "TRIGGER.THE_SANDBOX_CRASHED_DURING"); },
  get maxcalls() { return tStatic("logic.functions", "TRIGGER.THE_SANDBOX_REACHED_ITS"); },
  get maxage() { return tStatic("logic.functions", "TRIGGER.THE_SANDBOX_REACHED_ITS_2"); },
  get memory() { return tStatic("logic.functions", "TRIGGER.THE_SANDBOX_S_MEMORY"); },
  get protocol() { return tStatic("logic.functions", "TRIGGER.THE_SANDBOX_SENT_SOMETHING"); },
};

/**
 * A plain sentence for why a warm sandbox was replaced after a call. `dirty:<leftovers>` means the
 * function left work running after it answered; the other values are limits or failures. Unknown
 * values get null — the raw reason is still shown beside it.
 */
export const explainDiscardReason = (reason?: string | null): string | null => {
  const value = reason?.trim();
  if (!value) return null;
  if (value.toLowerCase().startsWith("dirty")) {
    const leftovers = value
      .slice(value.indexOf(":") + 1 || value.length)
      .split(/[,;|\s]+/)
      .map((item) => item.trim())
      .filter(Boolean);
    const labels = Array.from(
      new Set(
        leftovers
          .map((item) => LEFTOVER_LABELS.find((entry) => entry.match.test(item))?.label)
          .filter((label): label is string => !!label),
      ),
    );
    // `late`: code that an EARLIER call left behind ran during this one — a different message.
    const late = leftovers.some((item) => /^late$/i.test(item));
    const what = labels.length > 0 ? labels.join(", ") : late ? null : "some work";
    const parts: string[] = [];
    if (what) parts.push(tStatic("logic.functions", "TRIGGER.THE_FUNCTION_LEFT_RUNNING", { what }));
    if (late) parts.push(tStatic("logic.functions", "TRIGGER.CODE_LEFT_RUNNING_BY"));
    parts.push(
      tStatic("logic.functions", "TRIGGER.AWAIT_EVERY_CALL_OR"),
    );
    return parts.join(" ");
  }
  return OTHER_DISCARD_REASONS[value.toLowerCase()] ?? null;
};
