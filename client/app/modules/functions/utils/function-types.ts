/**
 * The type declarations the Code tab feeds Monaco so `input`, `ctx` and the tenant's own
 * variables complete and hover like they would in VS Code. This mirrors the runner's
 * `bootstrap.mjs` contract — it is documentation the editor can read, and nothing here reaches
 * the sandbox.
 */

const IDENTIFIER = /^[A-Za-z_$][A-Za-z0-9_$]*$/;

/** A key that is not a bare identifier still has to be reachable as `ctx.env["odd key"]`. */
const quoteKey = (key: string) =>
  IDENTIFIER.test(key) ? key : `"${key.replace(/\\/g, "\\\\").replace(/"/g, '\\"')}"`;

/** `ctx.env` is generated per function, so the keys under Configuration are the ones offered. */
const envMembers = (envKeys: string[]) => {
  const keys = Array.from(new Set(envKeys.map((key) => key.trim()).filter(Boolean)));
  if (keys.length === 0) {
    return "  /** No variables are bound yet — add them under Configuration. */\n  [key: string]: string | undefined;";
  }
  return [
    ...keys.map(
      (key) => `  /** Variable ${key}, snapshotted at deploy. */\n  ${quoteKey(key)}: string;`,
    ),
    "  /** Variables added after this deploy. */\n  [key: string]: string | undefined;",
  ].join("\n");
};

export const FUNCTION_TYPES_PATH = "ts:blocks-functions/context.d.ts";

export const buildFunctionTypeDefs = (envKeys: string[] = []): string => `
/**
 * Everything the sandbox hands a function, fresh for every call.
 *
 * HTTP calls of a deployed function reuse a warm sandbox: module-level code runs once and its
 * variables live on to the next call — maybe another user's. Keep connections and caches at module
 * level; keep request data (input, ctx, tokens) inside the handler. Await everything, or hand work
 * that may finish after the answer to ctx.waitUntil().
 */
declare interface FunctionContext {
  /** Who invoked this run. Empty identity on a public call — never a privileged token. */
  readonly context: FunctionCallerContext;
  /** Call Blocks APIs as the caller. */
  readonly blocks: FunctionBlocksAccess;
  /** Your variables, as plain strings. A secret-bound variable arrives as its real value, masked in logs. */
  readonly env: FunctionEnv;
  /** This run's own metadata. */
  readonly run: FunctionRun;
  /** Structured log lines kept on the run; \`console\` is captured too. */
  readonly log: FunctionLogger;
  /**
   * Finish work after the answer is sent: the caller gets the result at once, the promise runs on
   * (within the time limit) and the sandbox is not reused until it settles. Use it instead of a
   * promise you do not await — work still running after the answer makes the sandbox be replaced.
   * @example ctx.waitUntil(fetch(auditUrl, { method: "POST", body }));
   */
  waitUntil(promise: Promise<unknown>): void;
}

declare interface FunctionCallerContext {
  readonly tenantId: string;
  /** null when the trigger is public. */
  readonly userId: string | null;
  readonly roles: string[];
  readonly permissions: string[];
  readonly isAuthenticated: boolean;
}

declare interface FunctionBlocksAccess {
  /**
   * A fresh, short-lived Blocks access token for the user who invoked this run — pass it as the
   * \`Authorization: Bearer\` of a Blocks API call, with \`ctx.context.tenantId\` as \`x-blocks-key\`.
   * In a warm sandbox it is fetched only when you call this (one IAM round trip, once per call;
   * later calls in the same run get the same token). Resolves to \`undefined\` when there is no
   * signed-in caller (a public trigger, a scheduled workflow) or none could be issued. Masked in logs.
   */
  getAccessToken(): Promise<string | undefined>;
}

declare interface FunctionEnv {
${envMembers(envKeys)}
}

declare interface FunctionRun {
  readonly id: string;
  /** Active version number this run executed on. */
  readonly version: number;
  /** 1 for the first try; higher when the retry policy re-ran it. */
  readonly attempt: number;
  readonly invokedBy: {
    readonly type: "http" | "workflow" | "test" | "replay";
    /** What started the run (e.g. the workflow), or null when there is none. */
    readonly id: string | null;
  };
}

declare interface FunctionLogger {
  debug(message: string, data?: unknown): void;
  info(message: string, data?: unknown): void;
  warn(message: string, data?: unknown): void;
  error(message: string, data?: unknown): void;
}

/**
 * What an HTTP or Test invocation hands the handler. Anything after \`/fn/{id}\` is \`path\`; the
 * body arrives parsed when it is JSON. A workflow node passes the previous node's output instead.
 */
declare interface FunctionInput {
  /** One of the trigger's accepted methods — any other is refused with 405 before the handler runs. */
  readonly method: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  /** \`orders/42\` for a call to \`…/fn/{id}/orders/42\`; \`\` for the root. */
  readonly path: string;
  /** A repeated key is an array. */
  readonly query: Record<string, string | string[]>;
  /** Lower-cased. Content negotiation, provenance and webhook signatures — never a credential. */
  readonly headers: Record<string, string>;
  /** Parsed JSON, the raw text for other content types, null for no body (always null on GET). */
  readonly body: unknown;
  /**
   * The exact request bytes, base64 — what a webhook signature is computed over. Null with no
   * body, or when the body is too large to carry twice.
   */
  readonly rawBody: string | null;
}

/**
 * The shape the runner expects as the module's default export.
 *
 * \`input\` and \`ctx\` are **parameters**: they exist only inside the handler. Top-level code runs
 * once when the sandbox starts — the place for clients, caches and constants — and referencing
 * \`ctx\` there fails the run while the module is still loading, before the handler is called.
 */
declare type FunctionHandler = (input: FunctionInput, ctx: FunctionContext) =>
  unknown | Promise<unknown> | AsyncIterable<unknown>; // an async iterable streams (async function*)

/**
 * Return this from the handler to control the HTTP answer when the trigger's Response is
 * "Wait for answer (API)". Anything else you return is sent as 200 + JSON.
 *
 * - \`statusCode\` 200–599 (1xx is refused). 204/205/304 never carry a body.
 * - \`headers\`: only content and caching headers pass (content-type, content-language,
 *   content-disposition, cache-control, expires, last-modified, etag, vary, retry-after),
 *   \`location\` on a 3xx to an http(s) or relative URL, and \`x-*\` (except x-forwarded-*,
 *   x-real-ip, x-original-*, x-blocks-*). Header count and size are capped. \`set-cookie\` is always
 *   dropped. X-Content-Type-Options: nosniff and Content-Security-Policy: sandbox are always added.
 * - \`body\`: a string is sent as is; anything else as JSON.
 *
 * A failed run answers 502, a timed-out one 504 (\`{ error, runId }\`). A call that takes too long
 * gets 202 + a poll token instead — the run still finishes.
 * @example return { statusCode: 404, body: { error: "order not found" } };
 */
declare interface FunctionHttpResponse {
  statusCode: number;
  headers?: Record<string, string | string[]>;
  body?: unknown;
}

// Node globals the sandbox provides that the browser libs do not describe.
declare const process: {
  readonly env: Record<string, string | undefined>;
  readonly version: string;
  hrtime: { bigint(): bigint };
  memoryUsage(): { rss: number; heapUsed: number; heapTotal: number; external: number };
};
declare const Buffer: any;
`;

/** `ctx.<member>` completions, which work even in a file with no JSDoc type annotations. */
export const buildCtxCompletions = (envKeys: string[] = []) => {
  const keys = Array.from(new Set(envKeys.map((key) => key.trim()).filter(Boolean)));
  return [
    {
      label: "env",
      detail: "Record<string, string>",
      documentation: "Your variables, as plain strings.",
    },
    {
      label: "log",
      detail: "FunctionLogger",
      documentation: "info / warn / error / debug — kept on the run.",
    },
    { label: "run", detail: "FunctionRun", documentation: "id, version, attempt, invokedBy." },
    {
      label: "context",
      detail: "FunctionCallerContext",
      documentation: "tenantId, userId, roles, permissions, isAuthenticated.",
    },
    {
      label: "blocks",
      detail: "FunctionBlocksAccess",
      documentation: "getAccessToken() — the caller's Blocks token, fetched on demand (await it), or undefined.",
    },
    {
      label: "waitUntil",
      detail: "(promise: Promise<unknown>) => void",
      documentation:
        "Finish work after the answer is sent, without making the reused sandbox be replaced.",
    },
    ...keys.map((key) => ({
      label: `env.${key}`,
      detail: "string",
      documentation: `Variable ${key}, snapshotted at deploy.`,
    })),
  ];
};
