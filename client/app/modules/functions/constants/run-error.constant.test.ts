import { describe, expect, it } from "vitest";
import { explainRunError } from "./run-error.constant";

describe("explainRunError", () => {
  it("has nothing to add without a code or a message", () => {
    expect(explainRunError(null)).toBeNull();
    expect(explainRunError(undefined, undefined)).toBeNull();
    expect(explainRunError("SomethingNewFromTheRunner")).toBeNull();
  });

  it("explains a code on its own", () => {
    expect(explainRunError("TimedOut")).toMatch(/passed its timeout/);
    expect(explainRunError("UserRuntimeError")).toMatch(/handler threw/);
  });

  it("names the handler's scope when ctx or input was used at module level", () => {
    // The screenshot case: the run failed before the handler ran, so "the stack is in the logs"
    // was pointing at logs that could not exist.
    for (const name of ["ctx", "input"]) {
      const hint = explainRunError(
        "UserRuntimeError",
        `the function module failed to load: ${name} is not defined`,
      );
      expect(hint).toMatch(/parameters of your handler/);
      expect(hint).toMatch(/export default async function handler\(input, ctx\)/);
      expect(hint).not.toMatch(/logs below/);
    }
  });

  it("points a failed import at package.json rather than at the handler", () => {
    const hint = explainRunError(
      "UserRuntimeError",
      "the function module failed to load: Cannot find package 'ky' imported from /function/index.js",
    );
    expect(hint).toMatch(/Add the package to package\.json/);
  });

  it("still says the handler never ran for any other module-load failure", () => {
    const hint = explainRunError("UserRuntimeError", "the function module failed to load: boom");
    expect(hint).toMatch(/threw while it was being imported/);
    expect(hint).toMatch(/nothing was logged/);
  });

  it("leaves a genuine handler throw with the code's own sentence", () => {
    expect(explainRunError("UserRuntimeError", "ctx is not defined")).toMatch(/handler threw/);
  });
  it("explains a broken secret reference as the author's to fix and a store outage as safe to rerun", () => {
    // The runner's message names the variable; the hint must not contradict it by promising a retry.
    const unresolved = explainRunError(
      "SecretUnresolved",
      "variable STRIPE_KEY references secret 'sec_1', but it has been deleted",
    );
    expect(unresolved).toMatch(/Nothing ran/);
    expect(unresolved).toMatch(/Rebind the variable/);
    expect(unresolved).not.toMatch(/retried automatically/);

    const outage = explainRunError("SecretStoreUnavailable", "the key vault could not be read");
    expect(outage).toMatch(/nothing ran/);
    expect(outage).toMatch(/retried automatically/);
  });
});
