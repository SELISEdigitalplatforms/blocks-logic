import { describe, expect, it } from "vitest";
import { explainDiscardReason, withTriggerDefaults } from "./trigger";
import { acceptedHttpVerbs } from "../constants/endpoint.constant";
import { ITriggerConfig } from "../types/function.types";

/** A trigger as a function saved before reuse / response mode / verb lists existed sends it. */
const legacy = {
  httpEnabled: true,
  httpMethod: "Get",
  authMode: "Token",
  roles: [],
  permissions: [],
  roleMatch: "Any",
  permissionMatch: "Any",
  combine: "Or",
  workflowEnabled: true,
} as unknown as ITriggerConfig;

describe("withTriggerDefaults", () => {
  it("fills the new settings with their defaults for an old function", () => {
    expect(withTriggerDefaults(legacy)).toEqual({
      ...legacy,
      httpMethods: [],
      reuseSandbox: false,
      responseMode: "async",
    });
  });

  it("drops the removed cookie option if an older server still sends it", () => {
    const next = withTriggerDefaults({ ...legacy, allowSetCookie: true } as never);
    expect("allowSetCookie" in next).toBe(false);
  });

  it("keeps values the server sent", () => {
    const next = withTriggerDefaults({
      ...legacy,
      httpMethods: ["PUT", "GET"],
      reuseSandbox: true,
      responseMode: "sync",
    });
    expect(next.httpMethods).toEqual(["GET", "PUT"]);
    expect(next.reuseSandbox).toBe(true);
    expect(next.responseMode).toBe("sync");
  });

  it("drops verbs it does not know and accepts any casing", () => {
    const next = withTriggerDefaults({
      ...legacy,
      httpMethods: ["patch", "TRACE"] as unknown as ITriggerConfig["httpMethods"],
    });
    expect(next.httpMethods).toEqual(["PATCH"]);
  });

  it("treats an unknown response mode as background", () => {
    const next = withTriggerDefaults({
      ...legacy,
      responseMode: "later" as unknown as ITriggerConfig["responseMode"],
    });
    expect(next.responseMode).toBe("async");
  });
});

describe("acceptedHttpVerbs", () => {
  it("is the single legacy method when the list is empty", () => {
    expect(acceptedHttpVerbs({ httpMethod: "Get", httpMethods: [] })).toEqual(["GET"]);
  });

  it("is the list, in GET → DELETE order, when there is one", () => {
    expect(acceptedHttpVerbs({ httpMethod: "Get", httpMethods: ["DELETE", "POST"] })).toEqual([
      "POST",
      "DELETE",
    ]);
  });

  it("survives a trigger with no list at all", () => {
    expect(
      acceptedHttpVerbs({ httpMethod: "Post" } as Pick<
        ITriggerConfig,
        "httpMethod" | "httpMethods"
      >),
    ).toEqual(["POST"]);
  });
});

describe("explainDiscardReason", () => {
  it("is null for no reason", () => {
    expect(explainDiscardReason(null)).toBeNull();
    expect(explainDiscardReason("  ")).toBeNull();
  });

  it("names leftover timers and requests in plain words", () => {
    expect(explainDiscardReason("dirty:Timeout,HTTPCLIENTREQUEST")).toBe(
      "The function left a timer, an HTTP request running after it answered. Await every call, or use ctx.waitUntil() for work that should finish after the answer.",
    );
  });

  it("says the same thing once for several timers", () => {
    expect(explainDiscardReason("dirty:Timeout,Interval,Immediate")).toMatch(
      /^The function left a timer running/,
    );
  });

  it("still explains a dirty reason with leftovers it does not know", () => {
    expect(explainDiscardReason("dirty:SOMETHINGNEW")).toMatch(/left some work running/);
    expect(explainDiscardReason("dirty")).toMatch(/left some work running/);
  });

  it("explains the limits and failures", () => {
    expect(explainDiscardReason("maxCalls")).toMatch(/call limit/);
    expect(explainDiscardReason("memory")).toMatch(/memory/);
    expect(explainDiscardReason("timeout")).toMatch(/time limit/);
  });

  it("is null for a reason it does not know, so the raw value stands alone", () => {
    expect(explainDiscardReason("solar-flare")).toBeNull();
  });
});
