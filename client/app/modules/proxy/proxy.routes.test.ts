import { describe, expect, it } from "vitest";
import {
  mapProxyDetailDtoToProxy,
  mapProxyToCreatePayload,
  mapProxyToUpdatePayload,
} from "./mappers/proxy.mapper";
import { parseRouteTemplate, proxyFormSchema, proxyFormDefaultValues } from "./utils";
import type { ProxyDetailDto, ProxyFormValues } from "./types";

const detailDto = (routes: unknown): ProxyDetailDto =>
  ({
    itemId: "p1",
    name: "P",
    slug: "p",
    path: "",
    upstream: "https://api.x.com/v1",
    upstreamMasked: "https://api.x.com/v1",
    methods: ["GET", "POST"],
    enabled: true,
    headers: [],
    query: [],
    bodyMerge: [],
    methodConfigs: [],
    routes,
    currentVersion: 1,
    createdDate: "2026-01-01T00:00:00Z",
    lastUpdatedDate: "2026-01-01T00:00:00Z",
  }) as unknown as ProxyDetailDto;

const formValues = (routes: ProxyFormValues["routes"]): ProxyFormValues => ({
  ...proxyFormDefaultValues,
  name: "P",
  upstreamUrl: "https://api.x.com/v1",
  methods: ["GET", "POST"],
  routes,
});

describe("proxy routes", () => {
  describe("payload", () => {
    it("sends routes on update, so an unrelated edit cannot wipe them", () => {
      // The server replaces the whole route list. An omitted `routes` is therefore a destructive
      // save: it narrows the proxy to its base path without the user asking.
      const payload = mapProxyToUpdatePayload(
        "p1",
        formValues([
          {
            method: "GET",
            path: "orders/{id}",
            upstreamPath: "v1/charges/{id}",
            headers: null,
            query: null,
            bodyMerge: null,
            responseMode: null,
            responseInclude: null,
          },
        ]),
      );

      expect(payload.routes).toEqual([
        {
          method: "GET",
          path: "orders/{id}",
          upstreamPath: "v1/charges/{id}",
          headers: null,
          query: null,
          bodyMerge: null,
          responseMode: null,
          responseInclude: null,
          resilience: null,
        },
      ]);
    });

    it("sends an empty list when there are no routes", () => {
      expect(mapProxyToCreatePayload(formValues([])).routes).toEqual([]);
    });

    it("keeps inherit (null) distinct from explicit none ([]) on an override", () => {
      // Collapsing the two would turn a route that opted out of the proxy-wide body merge back
      // into one that inherits it — a silent behaviour change on save.
      const payload = mapProxyToCreatePayload(
        formValues([
          {
            method: "POST",
            path: "refunds",
            upstreamPath: null,
            headers: null,
            query: null,
            bodyMerge: [],
            responseMode: null,
            responseInclude: null,
          },
        ]),
      );

      expect(payload.routes[0].headers).toBeNull();
      expect(payload.routes[0].bodyMerge).toEqual([]);
    });
  });

  describe("reading a proxy back", () => {
    it("round-trips every route member", () => {
      const proxy = mapProxyDetailDtoToProxy(
        detailDto([
          {
            method: "post",
            path: "/orders/{id}/",
            upstreamPath: "/v1/charges/{id}/",
            headers: [{ key: "X-Scope", value: "orders" }],
            query: null,
            bodyMerge: [],
            responseMode: "Select",
            responseInclude: ["id"],
          },
        ]),
      );

      expect(proxy.routes).toEqual([
        {
          method: "POST",
          path: "orders/{id}",
          upstreamPath: "v1/charges/{id}",
          headers: [{ key: "X-Scope", value: "orders" }],
          query: null,
          bodyMerge: [],
          responseMode: "select",
          responseInclude: ["id"],
          resilience: null,
        },
      ]);
    });

    it("round-trips a route's resilience, which the form cannot yet edit", () => {
      // The console rebuilds every route field by field on save. A member it does not carry is a
      // member it deletes — so a policy set through the API would vanish the first time somebody
      // renamed a header in the UI.
      const proxy = mapProxyDetailDtoToProxy(
        detailDto([
          {
            method: "get",
            path: "orders",
            resilience: {
              timeoutSeconds: 9,
              retry: { attempts: 2, backoff: "Exponential", initialDelaySeconds: 3, idempotent: true },
              breaker: { failureThreshold: 4, openSeconds: 20 },
            },
          },
        ]),
      );

      expect(proxy.routes[0].resilience).toEqual({
        timeoutSeconds: 9,
        retry: { attempts: 2, backoff: "exponential", initialDelaySeconds: 3, idempotent: true },
        breaker: { failureThreshold: 4, openSeconds: 20 },
      });
    });

    it("reads an empty resilience object as not configured", () => {
      // "Configured with nothing" and "not configured" are the same state on the server, and the
      // difference matters: only the second one inherits.
      const proxy = mapProxyDetailDtoToProxy(
        detailDto([{ method: "get", path: "orders", resilience: {} }]),
      );

      expect(proxy.routes[0].resilience).toBeNull();
    });

    it("treats a missing routes field as no routes", () => {
      expect(mapProxyDetailDtoToProxy(detailDto(undefined)).routes).toEqual([]);
    });
  });

  describe("template grammar", () => {
    it.each(["", "charges", "charges/{id}", "a/{b}/c/{d_1}"])("accepts %j", (template) => {
      expect(parseRouteTemplate(template).ok).toBe(true);
    });

    it.each([
      ["../admin", "'.' or '..'"],
      ["a/./b", "'.' or '..'"],
      ["a//b", "empty segment"],
      ["charges/x{id}", "whole segment"],
      ["charges/{id}/{id}", "declared twice"],
    ])("rejects %j", (template, reason) => {
      const parsed = parseRouteTemplate(template);
      expect(parsed.ok).toBe(false);
      if (!parsed.ok) expect(parsed.reason).toContain(reason);
    });

    it("collects parameter names in order", () => {
      const parsed = parseRouteTemplate("charges/{id}/refunds/{refundId}");
      expect(parsed.ok && parsed.params).toEqual(["id", "refundId"]);
    });
  });

  describe("form validation", () => {
    const parse = (routes: ProxyFormValues["routes"]) =>
      proxyFormSchema.safeParse(formValues(routes));

    const route = (over: Partial<ProxyFormValues["routes"][number]>) => ({
      method: "GET" as const,
      path: "charges/{id}",
      upstreamPath: null,
      headers: null,
      query: null,
      bodyMerge: null,
      responseMode: null,
      responseInclude: null,
      ...over,
    });

    it("accepts a well-formed route", () => {
      expect(parse([route({})]).success).toBe(true);
    });

    it("rejects a path that walks out of the upstream", () => {
      const result = parse([route({ path: "../admin" })]);
      expect(result.success).toBe(false);
    });

    it("rejects a method the proxy does not allow", () => {
      const result = parse([route({ method: "DELETE" })]);
      expect(result.success).toBe(false);
    });

    it("rejects two routes with the same method and path", () => {
      const result = parse([route({}), route({ path: "/charges/{id}/" })]);
      expect(result.success).toBe(false);
    });

    it("rejects an upstream parameter the client path does not declare", () => {
      const result = parse([route({ upstreamPath: "v1/charges/{other}" })]);
      expect(result.success).toBe(false);
    });

    it("accepts an upstream path that reuses the declared parameter", () => {
      expect(parse([route({ upstreamPath: "v1/charges/{id}" })]).success).toBe(true);
    });
  });
});
