import { describe, expect, it } from "vitest";
import { payloadOf } from "./test-input";

describe("payloadOf", () => {
  it("lifts the body back out of a stored POST run, so Test does not wrap it twice", () => {
    const stored = JSON.stringify({
      method: "POST",
      path: "",
      query: {},
      headers: { "content-type": "application/json" },
      body: { action: "push", id: "order-1" },
    });

    expect(JSON.parse(payloadOf(stored))).toEqual({ action: "push", id: "order-1" });
  });

  it("takes the query of a stored GET run", () => {
    const stored = JSON.stringify({ method: "GET", path: "", query: { id: "7" }, headers: {}, body: null });

    expect(JSON.parse(payloadOf(stored))).toEqual({ id: "7" });
  });

  it("gives an empty object for a run that had no body", () => {
    expect(payloadOf(JSON.stringify({ method: "POST", body: null }))).toBe("{}");
  });

  it.each(['{"action":"pull"}', "not json", "[1,2]"])("leaves anything else as it is: %s", (value) => {
    expect(payloadOf(value)).toBe(value);
  });
});
