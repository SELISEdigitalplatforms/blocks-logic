import { describe, expect, it } from "vitest";
import { buildCtxCompletions, buildFunctionTypeDefs } from "./function-types";

describe("buildFunctionTypeDefs", () => {
  it("declares the contract the runner actually gives a handler", () => {
    const dts = buildFunctionTypeDefs();

    expect(dts).toContain("declare interface FunctionContext");
    expect(dts).toContain("readonly env: FunctionEnv");
    expect(dts).toContain("readonly run: FunctionRun");
    expect(dts).toContain("readonly log: FunctionLogger");
    expect(dts).toContain("readonly isAuthenticated: boolean");
    expect(dts).toContain("info(message: string, data?: unknown): void");
  });

  it("types each bound variable as a known key", () => {
    const dts = buildFunctionTypeDefs(["FX_API_BASE", "LEDGER_ACCOUNT"]);

    expect(dts).toContain("FX_API_BASE: string;");
    expect(dts).toContain("LEDGER_ACCOUNT: string;");
    // Variables added after this deploy still resolve, rather than erroring.
    expect(dts).toContain("[key: string]: string | undefined;");
  });

  it("says so when nothing is bound", () => {
    expect(buildFunctionTypeDefs([])).toContain("No variables are bound yet");
  });

  it("quotes a key that is not a bare identifier so the declaration stays valid", () => {
    const dts = buildFunctionTypeDefs(["ODD KEY", 'WITH"QUOTE']);

    expect(dts).toContain('"ODD KEY": string;');
    expect(dts).toContain('"WITH\\"QUOTE": string;');
  });

  it("ignores blanks and duplicates rather than emitting a broken member list", () => {
    const dts = buildFunctionTypeDefs(["API_BASE", "API_BASE", "  ", ""]);

    expect(dts.match(/API_BASE: string;/g)).toHaveLength(1);
    expect(dts).not.toContain("  : string;");
  });

  it("declares the Node globals the browser libs do not describe", () => {
    const dts = buildFunctionTypeDefs();
    expect(dts).toContain("declare const process:");
    expect(dts).toContain("declare const Buffer:");
  });
});

describe("buildCtxCompletions", () => {
  it("offers the ctx members even with no variables bound", () => {
    expect(buildCtxCompletions().map((item) => item.label)).toEqual([
      "env",
      "log",
      "run",
      "context",
    ]);
  });

  it("offers each bound variable under env", () => {
    const labels = buildCtxCompletions(["FX_API_BASE"]).map((item) => item.label);
    expect(labels).toContain("env.FX_API_BASE");
  });
});
