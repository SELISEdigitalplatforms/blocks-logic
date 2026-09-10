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
      (key) =>
        `  /** Variable ${key}, snapshotted at deploy. */\n  ${quoteKey(key)}: string;`,
    ),
    "  /** Variables added after this deploy. */\n  [key: string]: string | undefined;",
  ].join("\n");
};

export const FUNCTION_TYPES_PATH = "ts:blocks-functions/context.d.ts";

export const buildFunctionTypeDefs = (envKeys: string[] = []): string => `
/** Everything the sandbox hands a function. */
declare interface FunctionContext {
  /** Who invoked this run. Empty identity on a public call — never a privileged token. */
  readonly context: FunctionCallerContext;
  /** Your variables, as plain strings. Secrets are never here. */
  readonly env: FunctionEnv;
  /** This run's own metadata. */
  readonly run: FunctionRun;
  /** Structured log lines kept on the run; \`console\` is captured too. */
  readonly log: FunctionLogger;
}

declare interface FunctionCallerContext {
  readonly tenantId: string;
  /** null when the trigger is public. */
  readonly userId: string | null;
  readonly roles: string[];
  readonly permissions: string[];
  readonly isAuthenticated: boolean;
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
  readonly invokedBy: "http" | "workflow" | "test" | "replay" | "schedule" | "event";
}

declare interface FunctionLogger {
  debug(message: string, data?: unknown): void;
  info(message: string, data?: unknown): void;
  warn(message: string, data?: unknown): void;
  error(message: string, data?: unknown): void;
}

/** The shape the runner expects as the module's default export. */
declare type FunctionHandler = (input: unknown, ctx: FunctionContext) => unknown | Promise<unknown>;

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
    { label: "env", detail: "Record<string, string>", documentation: "Your variables, as plain strings." },
    { label: "log", detail: "FunctionLogger", documentation: "info / warn / error / debug — kept on the run." },
    { label: "run", detail: "FunctionRun", documentation: "id, version, attempt, invokedBy." },
    {
      label: "context",
      detail: "FunctionCallerContext",
      documentation: "tenantId, userId, roles, permissions, isAuthenticated.",
    },
    ...keys.map((key) => ({
      label: `env.${key}`,
      detail: "string",
      documentation: `Variable ${key}, snapshotted at deploy.`,
    })),
  ];
};
