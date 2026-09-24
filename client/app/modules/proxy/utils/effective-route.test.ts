import { describe, expect, it } from "vitest";
import { ProxyRoute } from "../types";
import { keyCollisions, resolveEffectiveRoute } from "./effective-route";

const route = (overrides: Partial<ProxyRoute> = {}): ProxyRoute => ({
  method: "GET",
  path: "",
  upstreamPath: null,
  headers: null,
  query: null,
  bodyMerge: null,
  responseMode: null,
  responseInclude: null,
  ...overrides,
});

const proxy = {
  upstreamUrl: "https://api.vendor.test/v1",
  headers: [{ key: "X-Api-Key", value: "{{$VAR.key}}" }],
  query: [{ key: "api_key", value: "abc" }],
  bodyMerge: [{ key: "tenant", value: "acme" }],
  methodConfigs: [],
  responseMode: "all" as const,
  responseInclude: [],
};

describe("resolveEffectiveRoute", () => {
  it("adds endpoint headers on top of the connection's, tagging each row with its source", () => {
    const result = resolveEffectiveRoute(
      proxy,
      "GET",
      route({ headers: [{ key: "X-Trace", value: "1" }] }),
    );
    expect(result.headers).toEqual([
      { key: "X-Api-Key", value: "{{$VAR.key}}", source: "connection" },
      { key: "X-Trace", value: "1", source: "endpoint" },
    ]);
  });

  it("replaces a same-name header in place, ignoring case", () => {
    const result = resolveEffectiveRoute(
      proxy,
      "GET",
      route({ headers: [{ key: "x-api-key", value: "other" }] }),
    );
    expect(result.headers).toEqual([{ key: "x-api-key", value: "other", source: "endpoint" }]);
  });

  it("matches query keys exactly, so keys differing only in case are both sent", () => {
    const sameCase = resolveEffectiveRoute(
      proxy,
      "GET",
      route({ query: [{ key: "api_key", value: "x" }] }),
    );
    expect(sameCase.query).toEqual([{ key: "api_key", value: "x", source: "endpoint" }]);

    const otherCase = resolveEffectiveRoute(
      proxy,
      "GET",
      route({ query: [{ key: "API_KEY", value: "x" }] }),
    );
    expect(otherCase.query.map((row) => row.key)).toEqual(["api_key", "API_KEY"]);
  });

  it("collapses duplicate keys inside one list, last row winning", () => {
    const result = resolveEffectiveRoute(
      {
        ...proxy,
        headers: [
          { key: "X-A", value: "1" },
          { key: "x-a", value: "2" },
        ],
      },
      "GET",
      null,
    );
    expect(result.headers).toEqual([{ key: "x-a", value: "2", source: "connection" }]);
  });

  it("layers the legacy per-method override between the connection and the endpoint", () => {
    const result = resolveEffectiveRoute(
      {
        ...proxy,
        methodConfigs: [
          {
            method: "POST",
            upstream: "https://other.test",
            headers: [
              { key: "X-Api-Key", value: "post-key" },
              { key: "X-M", value: "m" },
            ],
            query: null,
          },
        ],
      },
      "POST",
      route({ method: "POST", headers: [{ key: "X-M", value: "e" }] }),
    );
    expect(result.upstreamUrl).toBe("https://other.test");
    expect(result.headers).toEqual([
      { key: "X-Api-Key", value: "post-key", source: "method" },
      { key: "X-M", value: "e", source: "endpoint" },
    ]);
  });

  it("ignores a per-method override for a different method", () => {
    const result = resolveEffectiveRoute(
      {
        ...proxy,
        methodConfigs: [
          { method: "POST", upstream: "https://other.test", headers: null, query: null },
        ],
      },
      "GET",
      route(),
    );
    expect(result.upstreamUrl).toBe(proxy.upstreamUrl);
  });

  it("takes body merge from the endpoint when set, including an explicit empty list", () => {
    expect(resolveEffectiveRoute(proxy, "POST", route({ method: "POST" })).bodyMerge).toEqual(
      proxy.bodyMerge,
    );
    expect(
      resolveEffectiveRoute(
        proxy,
        "POST",
        route({ method: "POST", bodyMerge: [{ key: "a", value: "b" }] }),
      ).bodyMerge,
    ).toEqual([{ key: "a", value: "b" }]);
    expect(
      resolveEffectiveRoute(proxy, "POST", route({ method: "POST", bodyMerge: [] })).bodyMerge,
    ).toEqual([]);
  });

  it("takes the response shape from the endpoint, else the connection", () => {
    expect(resolveEffectiveRoute(proxy, "GET", route())).toMatchObject({
      responseMode: "all",
      responseInclude: [],
    });
    expect(
      resolveEffectiveRoute(
        proxy,
        "GET",
        route({ responseMode: "select", responseInclude: ["data.id"] }),
      ),
    ).toMatchObject({ responseMode: "select", responseInclude: ["data.id"] });
  });
});

describe("keyCollisions", () => {
  const connectionHeaders = [{ key: "X-Api-Key" }];

  it("flags a header that replaces a connection header, ignoring case", () => {
    expect(
      keyCollisions([{ key: "x-api-key" }, { key: "X-Other" }], connectionHeaders, "header"),
    ).toEqual([
      { replacesConnection: true, duplicateInList: false },
      { replacesConnection: false, duplicateInList: false },
    ]);
  });

  it("flags a query key only on an exact match", () => {
    expect(
      keyCollisions([{ key: "api_key" }, { key: "API_KEY" }], [{ key: "api_key" }], "query"),
    ).toEqual([
      { replacesConnection: true, duplicateInList: false },
      { replacesConnection: false, duplicateInList: false },
    ]);
  });

  it("flags every row of a duplicated key", () => {
    expect(
      keyCollisions([{ key: "X-Foo" }, { key: "x-foo " }, { key: "Y" }], [], "header"),
    ).toEqual([
      { replacesConnection: false, duplicateInList: true },
      { replacesConnection: false, duplicateInList: true },
      { replacesConnection: false, duplicateInList: false },
    ]);
    expect(keyCollisions([{ key: "Limit" }, { key: "limit" }], [], "query")).toEqual([
      { replacesConnection: false, duplicateInList: false },
      { replacesConnection: false, duplicateInList: false },
    ]);
  });

  it("trims keys and never matches an empty key", () => {
    expect(keyCollisions([{ key: "  " }, { key: "" }], [{ key: "" }], "header")).toEqual([
      { replacesConnection: false, duplicateInList: false },
      { replacesConnection: false, duplicateInList: false },
    ]);
    expect(
      keyCollisions([{ key: " api_key " }], [{ key: "api_key" }], "query")[0].replacesConnection,
    ).toBe(true);
  });
});
