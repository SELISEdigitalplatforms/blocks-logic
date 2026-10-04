import { beforeEach, describe, expect, it, vi } from "vitest";

const http = vi.hoisted(() => {
  const make = () => ({
    get: vi.fn().mockResolvedValue({ data: null }),
    post: vi.fn().mockResolvedValue({ data: null }),
    put: vi.fn().mockResolvedValue({ isSuccess: true }),
    patch: vi.fn().mockResolvedValue({ isSuccess: true }),
    delete: vi.fn().mockResolvedValue({ isSuccess: true }),
  });
  return { logicService: make(), agentsService: make(), dataService: make(), iamService: make() };
});

vi.mock("@/lib/http-client", () => ({ serviceInstances: http }));

const { FakeHttpError } = vi.hoisted(() => {
  class FakeHttpError extends Error {
    status: number;
    errors: Record<string, unknown>;
    constructor(status: number, errors: Record<string, unknown>) {
      super("http error");
      this.status = status;
      this.errors = errors;
    }
  }
  return { FakeHttpError };
});

vi.mock("@seliseblocks/genesis-os", () => ({ HttpError: FakeHttpError }));

import { proxyService } from "./proxy.service";

const { logicService } = http;

beforeEach(() => {
  vi.clearAllMocks();
});

describe("ProxyService HTTP wiring", () => {
  it("lists proxies with GET on the collection and maps the list projection", async () => {
    logicService.get.mockResolvedValueOnce({
      data: [
        {
          itemId: "p1",
          name: "Stripe",
          slug: "stripe",
          upstreamMasked: "api.st****.com/***",
          methods: ["GET"],
          enabled: true,
          injectedCredential: false,
          headerCount: 0,
          queryCount: 0,
          calls24h: 5,
          createdDate: "2026-09-01T00:00:00.000Z",
          lastUpdatedDate: "2026-09-01T00:00:00.000Z",
        },
      ],
      totalCount: 1,
    });

    const page = await proxyService.getAll({ searchKey: "stri" });

    expect(logicService.get).toHaveBeenCalledWith(
      "/api/Proxies?search=stri&pageSize=200&pageNumber=0",
    );
    expect(page.totalCount).toBe(1);
    expect(page.items).toEqual([expect.objectContaining({ id: "p1", calls24h: 5, headers: [] })]);
  });

  it("gets a proxy by id from the path and maps null to null", async () => {
    logicService.get.mockResolvedValueOnce({ data: null });
    expect(await proxyService.get("missing")).toBeNull();
    expect(logicService.get).toHaveBeenCalledWith("/api/Proxies/missing");
  });

  it("percent-encodes an id before putting it in the path", async () => {
    logicService.get.mockResolvedValueOnce({ data: null });
    await proxyService.get("a/b?c");
    expect(logicService.get).toHaveBeenCalledWith("/api/Proxies/a%2Fb%3Fc");
  });

  it("sends Create as { name, upstream, methods, headers, query, enabled }", async () => {
    logicService.post.mockResolvedValueOnce({ isSuccess: true, itemId: "p9" });

    const res = await proxyService.create({
      name: "GitHub Proxy",
      upstreamUrl: "https://api.github.com/repos",
      methods: ["GET"],
      headers: [],
      query: [],
      bodyMerge: [],
      bodyMode: "passthrough",
      methodConfigs: [],
    });

    expect(logicService.post).toHaveBeenCalledWith("/api/Proxies", {
      name: "GitHub Proxy",
      upstream: "https://api.github.com/repos",
      methods: ["GET"],
      headers: [],
      query: [],
      bodyMerge: [],
      methodConfigs: [],
      routes: [],
      responseMode: "All",
      responseInclude: [],
      access: {
        kind: "BlocksToken",
        combine: "Or",
        roles: { mode: "any", values: [] },
        permissions: { mode: "any", values: [] },
        organizationId: "",
      },
      resilience: null,
      enabled: true,
    });
    expect(res).toMatchObject({ isSuccess: true, itemId: "p9" });
  });

  it("turns a thrown HttpError envelope into an isSuccess:false result", async () => {
    logicService.post.mockRejectedValueOnce(
      new FakeHttpError(409, {
        isSuccess: false,
        errors: null,
        code: "PROXY_SLUG_CONFLICT",
        message: "A proxy with this slug already exists.",
      }),
    );

    const res = await proxyService.create({
      name: "Stripe",
      upstreamUrl: "https://api.stripe.com",
      methods: ["GET"],
      headers: [],
      query: [],
    });

    expect(res).toMatchObject({
      isSuccess: false,
      code: "PROXY_SLUG_CONFLICT",
      errors: "A proxy with this slug already exists.",
    });
  });

  it("PUTs and DELETEs one proxy by its path", async () => {
    await proxyService.update({
      id: "p1",
      values: {
        name: "Stripe",
        upstreamUrl: "https://api.stripe.com",
        methods: ["GET", "POST"],
        headers: [],
        query: [],
      },
    });
    expect(logicService.put).toHaveBeenCalledWith(
      "/api/Proxies/p1",
      expect.objectContaining({ itemId: "p1", methods: ["GET", "POST"] }),
    );

    await proxyService.delete("p1");
    expect(logicService.delete).toHaveBeenCalledWith("/api/Proxies/p1");
  });

  it("PATCHes only the enabled flag, with the id in the path rather than the body", async () => {
    await proxyService.toggle({ id: "p1", enabled: false });
    expect(logicService.patch).toHaveBeenCalledWith("/api/Proxies/p1", { enabled: false });
  });

  it("maps ProxyLogFilter to a statusClass query param on the executions sub-resource", async () => {
    logicService.get.mockResolvedValueOnce({ data: [], totalCount: 0 });
    await proxyService.getExecutions("p1", "server", {});
    expect(logicService.get).toHaveBeenCalledWith(
      "/api/Proxies/p1/executions?statusClass=5xx&pageSize=10&pageNumber=0",
    );
  });

  it("forwards the requested page and page size on the executions sub-resource", async () => {
    logicService.get.mockResolvedValueOnce({
      data: [],
      totalCount: 42,
    });
    const result = await proxyService.getExecutions("p1", "all", { page: 2, pageSize: 20 });
    expect(logicService.get).toHaveBeenCalledWith(
      "/api/Proxies/p1/executions?statusClass=all&pageSize=20&pageNumber=2",
    );
    expect(result).toEqual({ rows: [], totalCount: 42 });
  });

  it("returns a typed test response and degrades a 400 to a rendered result", async () => {
    logicService.post.mockResolvedValueOnce({
      ok: true,
      status: 200,
      outcome: "Success",
      latencyMs: 42,
      upstreamUrl: "https://api.stripe.com/v1/charges",
      upstreamHost: "api.stripe.com",
      injectedHeaderKeys: [],
      injectedQueryKeys: [],
      responseBody: "{}",
    });
    await expect(proxyService.test({ proxyId: "p1", method: "GET" })).resolves.toMatchObject({
      ok: true,
      status: 200,
      statusText: "OK",
    });

    logicService.post.mockRejectedValueOnce(
      new FakeHttpError(400, { upstream: "Use https:// endpoints only." }),
    );
    await expect(
      proxyService.test({
        method: "GET",
        draft: {
          name: "Bad",
          upstreamUrl: "http://example.com",
          methods: ["GET"],
          headers: [],
          query: [],
        },
      }),
    ).resolves.toMatchObject({ ok: false, status: 400 });
  });
});

describe("ProxyService OpenAPI preview", () => {
  it("posts the document and maps what the server says it would produce", async () => {
    logicService.post.mockResolvedValueOnce({
      baseUrl: "https://api.vendor.com/v1",
      operations: [
        {
          operationId: "getOrder",
          method: "get",
          path: "/orders/{id}",
          queryParameters: ["expand"],
          securityHeaders: ["X-Api-Key"],
          alreadyExists: false,
        },
        // A verb the gateway cannot forward. The server reports the skip as a warning; listing it as
        // an unimportable row would make that warning read like a bug.
        { operationId: "traced", method: "TRACE", path: "/health" },
      ],
      warnings: ["TRACE /health is not a method this gateway can forward."],
    });

    const preview = await proxyService.previewOpenApi({ specJson: "{}", proxyId: "p1" });

    expect(logicService.post).toHaveBeenCalledWith("/api/Proxies/openapi/preview", {
      specJson: "{}",
      specUrl: null,
      proxyId: "p1",
    });
    expect(preview.operations).toHaveLength(1);
    expect(preview.operations[0]).toMatchObject({
      method: "GET",
      path: "orders/{id}",
      queryParameters: ["expand"],
      securityHeaders: ["X-Api-Key"],
    });
    expect(preview.warnings).toHaveLength(1);
  });

  it("a refused document comes back as reasons rather than as a thrown error", async () => {
    // These sentences are the answer to "why did my paste produce nothing", so the dialog needs them
    // in hand — not an exception to catch somewhere above it.
    logicService.post.mockRejectedValueOnce(
      new FakeHttpError(400, {
        code: "PROXY_VALIDATION",
        errors: ["That is valid JSON but not an OpenAPI document."],
      }),
    );

    const preview = await proxyService.previewOpenApi({ specJson: "{}" });

    expect(preview.errors).toEqual(["That is valid JSON but not an OpenAPI document."]);
    expect(preview.operations).toEqual([]);
  });

  it("says something true even when the failure carries no reasons", async () => {
    logicService.post.mockRejectedValueOnce(new Error("socket hang up"));

    expect((await proxyService.previewOpenApi({ specUrl: "https://x/openapi.json" })).errors)
      .toEqual(["The specification could not be read."]);
  });
});
