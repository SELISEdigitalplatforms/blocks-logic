import { describe, expect, it } from "vitest";
import { ProxyRoute } from "../types";
import { deriveProxyMethods, splitCredentialRows, toCredentialRows } from "./proxy.utils";

const route = (method: ProxyRoute["method"], path = ""): ProxyRoute => ({
  method,
  path,
  upstreamPath: null,
  headers: null,
  query: null,
  bodyMerge: null,
  responseMode: null,
  responseInclude: null,
});

describe("deriveProxyMethods", () => {
  it("is the set of methods the endpoints use, in first-seen order", () => {
    expect(
      deriveProxyMethods([route("POST", "orders"), route("GET", "orders/{id}"), route("POST", "refunds")]),
    ).toEqual(["POST", "GET"]);
  });

  it("falls back to GET when there are no endpoints, so the server never sees an empty list", () => {
    expect(deriveProxyMethods([])).toEqual(["GET"]);
  });
});

describe("credential rows", () => {
  it("round-trips headers and query through one list and back", () => {
    const rows = toCredentialRows(
      [{ key: "Authorization", value: "Bearer x" }],
      [{ key: "api_key", value: "{{$VAR.KEY}}" }],
    );
    expect(rows).toEqual([
      { key: "Authorization", value: "Bearer x", sendAs: "header" },
      { key: "api_key", value: "{{$VAR.KEY}}", sendAs: "query" },
    ]);
    expect(splitCredentialRows(rows)).toEqual({
      headers: [{ key: "Authorization", value: "Bearer x" }],
      query: [{ key: "api_key", value: "{{$VAR.KEY}}" }],
    });
  });

  it("drops blank rows and trims the rest", () => {
    expect(
      splitCredentialRows([
        { key: "  X-A ", value: " 1 ", sendAs: "header" },
        { key: "", value: "", sendAs: "header" },
        { key: "   ", value: "", sendAs: "query" },
      ]),
    ).toEqual({ headers: [{ key: "X-A", value: "1" }], query: [] });
  });
});
