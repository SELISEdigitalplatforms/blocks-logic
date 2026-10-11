import { HttpTriggerVerb, ITriggerConfig, TriggerResponseMode } from "../types/function.types";
import { FUNCTION_HTTP_VERBS } from "../constants/endpoint.constant";

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
  const responseMode: TriggerResponseMode = partial.responseMode === "sync" ? "sync" : "async";
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
  { match: /^(timeout|interval|immediate)$/i, label: "a timer" },
  { match: /^(httpclientrequest|httpincomingmessage|fetch)$/i, label: "an HTTP request" },
  { match: /^(fsreq|filehandle|statwatcher|fsevent)/i, label: "a file operation" },
  { match: /^(getaddrinfo|getnameinfo|querywrap)/i, label: "a DNS lookup" },
  { match: /^(tcpconnect|pipeconnect|shutdownwrap|writewrap)/i, label: "a network connection" },
  { match: /^processwrap$/i, label: "a child process" },
  { match: /^worker$/i, label: "a worker thread" },
  { match: /^waituntil$/i, label: "ctx.waitUntil() work that did not finish in time" },
];

const OTHER_DISCARD_REASONS: Record<string, string> = {
  timeout:
    "The call, or work passed to ctx.waitUntil(), ran past its time limit, so the sandbox was stopped.",
  drain: "A newer version went live, so this version's sandbox was retired after the call.",
  cancelled: "The run was cancelled.",
  crash: "The sandbox crashed during the call.",
  maxcalls: "The sandbox reached its call limit and was replaced with a fresh one.",
  maxage: "The sandbox reached its age limit and was replaced with a fresh one.",
  memory: "The sandbox's memory was close to its limit, so it was replaced with a fresh one.",
  protocol: "The sandbox sent something unexpected, so it was replaced.",
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
    if (what) parts.push(`The function left ${what} running after it answered.`);
    if (late) parts.push("Code left running by an earlier call ran during this call.");
    parts.push(
      "Await every call, or use ctx.waitUntil() for work that should finish after the answer.",
    );
    return parts.join(" ");
  }
  return OTHER_DISCARD_REASONS[value.toLowerCase()] ?? null;
};
