import { describe, expect, it } from "vitest";
import {
  mapLogFilterToStatusClass,
  mapMutationResponse,
  mapProxyDetailDtoToProxy,
  mapProxyExecutionListItemDtoToLog,
  mapProxyListItemDtoToProxy,
  mapProxyOverviewDtoToOverview,
  mapProxyTestRequestToPayload,
  mapProxyToCreatePayload,
  mapProxyToUpdatePayload,
  mapProxyVersionDtoToHistory,
} from "./mappers";
import {
  ProxyDetailDto,
  ProxyExecutionListItemDto,
  ProxyListItemDto,
  ProxyOverviewDto,
  ProxyVersionDto,
} from "./types";

describe("proxy mapper", () => {
  it("maps the GetAll list projection into the frontend proxy model", () => {
    const dto: ProxyListItemDto = {
      itemId: "p1",
      name: "Stripe Payments",
      slug: "stripe-payments",
      upstreamMasked: "api.st****.com/***",
      methods: ["GET", "POST", "TRACE"],
      enabled: true,
      injectedCredential: true,
      headerCount: 1,
      queryCount: 0,
      calls24h: 1248,
      createdDate: "2026-04-12T09:15:00.000Z",
      lastUpdatedDate: "2026-09-01T11:30:00.000Z",
    };

    expect(mapProxyListItemDtoToProxy(dto)).toMatchObject({
      id: "p1",
      slug: "stripe-payments",
      upstreamMasked: "api.st****.com/***",
      methods: ["GET", "POST"],
      enabled: true,
      headers: [],
      query: [],
      calls24h: 1248,
    });
  });

  it("maps the Get detail projection, storing the value verbatim", () => {
    const dto: ProxyDetailDto = {
      itemId: "p1",
      name: "Stripe Payments",
      slug: "stripe-payments",
      path: "/api/proxy/gateway/stripe-payments/*",
      upstream: "https://api.stripe.com/v1/charges",
      upstreamMasked: "api.st****.com/***",
      methods: ["GET"],
      enabled: false,
      headers: [{ key: "Authorization", value: "Bearer {{$VAR.stripe}}" }],
      query: [],
      currentVersion: 3,
      createdDate: "2026-04-12T09:15:00.000Z",
      lastUpdatedDate: "2026-09-01T11:30:00.000Z",
    };

    const proxy = mapProxyDetailDtoToProxy(dto);
    expect(proxy).toMatchObject({
      id: "p1",
      upstreamUrl: "https://api.stripe.com/v1/charges",
      enabled: false,
    });
    expect(proxy.headers[0]).toEqual({ key: "Authorization", value: "Bearer {{$VAR.stripe}}" });
  });

  it("keeps request-payload field-name assumptions in one place", () => {
    const values = {
      name: " Stripe Payments ",
      upstreamUrl: " https://api.stripe.com/v1/charges ",
      methods: ["GET", "POST"] as const,
      headers: [{ key: " Authorization ", value: " x " }],
      query: [{ key: "", value: "" }],
      bodyMerge: [],
      bodyMode: "passthrough" as const,
      methodConfigs: [],
    };

    expect(mapProxyToCreatePayload(values)).toEqual({
      name: "Stripe Payments",
      upstream: "https://api.stripe.com/v1/charges",
      methods: ["GET", "POST"],
      headers: [{ key: "Authorization", value: "x" }],
      query: [],
      bodyMerge: [],
      methodConfigs: [],
      enabled: true,
    });
    expect(mapProxyToUpdatePayload("p1", values)).toMatchObject({ itemId: "p1" });
    expect(mapProxyToUpdatePayload("p1", values)).not.toHaveProperty("enabled");
  });

  it("gates bodyMerge on the bodyMode tab and never emits bodyMode", () => {
    const base = {
      name: "P",
      upstreamUrl: "https://api.x.com",
      methods: ["POST"] as const,
      headers: [],
      query: [],
      bodyMerge: [{ key: " account ", value: " acct_1 " }],
      methodConfigs: [],
    };

    const merged = mapProxyToCreatePayload({ ...base, bodyMode: "merge" });
    expect(merged.bodyMerge).toEqual([{ key: "account", value: "acct_1" }]);
    expect(merged).not.toHaveProperty("bodyMode");

    const passthrough = mapProxyToCreatePayload({ ...base, bodyMode: "passthrough" });
    expect(passthrough.bodyMerge).toEqual([]);
    expect(passthrough).not.toHaveProperty("bodyMode");

    const test = mapProxyTestRequestToPayload({
      draft: { ...base, bodyMode: "passthrough" },
      method: "POST",
    });
    expect(test.draft?.bodyMerge).toEqual([]);
    expect(test.draft).not.toHaveProperty("bodyMode");
  });

  it("maps a detail DTO's bodyMerge, tolerating a missing/null list", () => {
    const withRows = mapProxyDetailDtoToProxy({
      itemId: "p1",
      name: "P",
      slug: "p",
      path: "/api/proxy/gateway/p/*",
      upstream: "https://api.x.com",
      upstreamMasked: "https://api.x.com",
      methods: ["POST"],
      enabled: true,
      headers: [],
      query: [],
      bodyMerge: [{ key: "account", value: "{{$VAR.a}}" }],
      methodConfigs: [],
      currentVersion: 1,
      createdDate: "2026-09-01T00:00:00.000Z",
      lastUpdatedDate: "2026-09-01T00:00:00.000Z",
    });
    expect(withRows.bodyMerge).toEqual([{ key: "account", value: "{{$VAR.a}}" }]);

    const withoutRows = mapProxyDetailDtoToProxy({
      itemId: "p2",
      name: "P",
      slug: "p",
      path: "/api/proxy/gateway/p/*",
      upstream: "https://api.x.com",
      upstreamMasked: "https://api.x.com",
      methods: ["GET"],
      enabled: true,
      headers: [],
      query: [],
      methodConfigs: [],
      currentVersion: 1,
      createdDate: "2026-09-01T00:00:00.000Z",
      lastUpdatedDate: "2026-09-01T00:00:00.000Z",
    });
    expect(withoutRows.bodyMerge).toEqual([]);
  });

  it("round-trips per-method overrides and prunes all-inherit / deselected entries", () => {
    const values = {
      name: "P",
      upstreamUrl: "https://api.x.com",
      methods: ["GET", "POST"] as const,
      headers: [],
      query: [],
      methodConfigs: [
        {
          method: "POST" as const,
          upstream: " https://api.x.com/v2 ",
          headers: [{ key: " X-Trace ", value: " on " }],
          query: [],
        },
        // all-inherit -> dropped
        { method: "GET" as const, upstream: null, headers: null, query: null },
        // deselected method -> dropped
        { method: "PUT" as const, upstream: "https://api.x.com/v3", headers: null, query: null },
      ],
    };

    expect(mapProxyToCreatePayload(values).methodConfigs).toEqual([
      {
        method: "POST",
        upstream: "https://api.x.com/v2",
        headers: [{ key: "X-Trace", value: "on" }],
        query: null,
      },
    ]);
  });

  it("maps a detail DTO's methodConfigs into the form model", () => {
    const proxy = mapProxyDetailDtoToProxy({
      itemId: "p1",
      name: "P",
      slug: "p",
      path: "/api/proxy/gateway/p/*",
      upstream: "https://api.x.com",
      upstreamMasked: "https://api.x.com",
      methods: ["GET", "POST"],
      enabled: true,
      headers: [],
      query: [],
      methodConfigs: [
        { method: "POST", upstream: "https://api.x.com/v2", headers: [{ key: "X-Trace", value: "on" }], query: null },
      ],
      currentVersion: 3,
      createdDate: "2026-09-01T00:00:00.000Z",
      lastUpdatedDate: "2026-09-01T00:00:00.000Z",
    });

    expect(proxy.methodConfigs).toEqual([
      {
        method: "POST",
        upstream: "https://api.x.com/v2",
        headers: [{ key: "X-Trace", value: "on" }],
        query: null,
      },
    ]);
  });

  it("normalises the server statusClass and version kind vocabularies", () => {
    expect(mapLogFilterToStatusClass("ok")).toBe("2xx");
    expect(mapLogFilterToStatusClass("client")).toBe("4xx");
    expect(mapLogFilterToStatusClass("server")).toBe("5xx");
    expect(mapLogFilterToStatusClass("all")).toBe("all");

    const version: ProxyVersionDto = {
      itemId: "v2",
      versionNumber: 2,
      kind: "ConfigUpdate",
      changeSummary: "Added payment intent expansion query.",
      changes: [
        { field: "upstream", label: "upstream", before: "https://a/v1", after: "https://a/v2" },
        { field: "header:X-Add", label: "header X-Add", before: null, after: "v" },
      ],
      who: "user-jane",
      whoName: "Jane Doe",
      whenUtc: "2026-09-01T11:30:00.000Z",
      versionLabel: "v2",
    };
    expect(mapProxyVersionDtoToHistory(version, "p1")).toMatchObject({
      id: "v2",
      proxyId: "p1",
      kind: "edit",
      summary: "Added payment intent expansion query.",
      actor: "user-jane",
      actorName: "Jane Doe",
      changes: [
        { field: "upstream", label: "upstream", before: "https://a/v1", after: "https://a/v2" },
        { field: "header:X-Add", label: "header X-Add", before: null, after: "v" },
      ],
    });
  });

  it("tolerates a missing or non-array changes list on a version row", () => {
    const version = {
      itemId: "v1",
      versionNumber: 1,
      kind: "Create",
      changeSummary: "Proxy created",
      who: "Jane",
      whenUtc: "2026-09-01T11:30:00.000Z",
      versionLabel: "v1",
    } as unknown as ProxyVersionDto;
    expect(mapProxyVersionDtoToHistory(version, "p1").changes).toEqual([]);
  });

  it("fills detail-only fields with defaults for the list row", () => {
    const dto: ProxyExecutionListItemDto = {
      itemId: "e1",
      startedAtUtc: "2026-09-01T11:30:00.000Z",
      requestMethod: "POST",
      requestPath: "/api/proxy/gateway/stripe-payments/charges",
      statusCode: 502,
      latencyMs: 120,
      outcome: "UpstreamError",
      upstreamHost: "api.stripe.com",
    };
    expect(mapProxyExecutionListItemDtoToLog(dto, "p1")).toMatchObject({
      id: "e1",
      proxyId: "p1",
      status: 502,
      statusText: "Bad Gateway",
      injectedHeaderKeys: [],
      responseBody: "",
    });
  });

  it("maps the overview tiles", () => {
    const dto: ProxyOverviewDto = {
      calls24h: 10,
      avgLatencyMs: 140,
      errorRatePct: 12.5,
      errorRateIsHigh: true,
      credentialRefs: ["STRIPE"],
      methods: ["GET", "POST"],
      lastCallAtUtc: "2026-09-01T11:30:00.000Z",
    };
    expect(mapProxyOverviewDtoToOverview(dto)).toMatchObject({
      calls24h: 10,
      avgLatencyMs: 140,
      errorRateIsHigh: true,
      methods: ["GET", "POST"],
    });
  });

  it("normalises a mutation envelope, falling back to message when errors is null", () => {
    expect(
      mapMutationResponse({ isSuccess: true, itemId: "p1", errors: null }),
    ).toMatchObject({ isSuccess: true, itemId: "p1" });

    expect(
      mapMutationResponse({
        isSuccess: false,
        itemId: null,
        errors: null,
        code: "PROXY_SLUG_CONFLICT",
        message: "A proxy with this slug already exists.",
      }),
    ).toMatchObject({
      isSuccess: false,
      errors: "A proxy with this slug already exists.",
      code: "PROXY_SLUG_CONFLICT",
    });
  });
});
