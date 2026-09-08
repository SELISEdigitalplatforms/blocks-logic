import { beforeEach, describe, expect, it, vi } from "vitest";

const http = vi.hoisted(() => {
  const make = () => ({
    get: vi.fn().mockResolvedValue({ data: null }),
    post: vi.fn().mockResolvedValue({ data: null }),
    put: vi.fn().mockResolvedValue({ isSuccess: true }),
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
  it("posts GetAll and maps the list projection", async () => {
    logicService.post.mockResolvedValueOnce({
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

    const proxies = await proxyService.getAll({ searchKey: "stri" });

    expect(logicService.post).toHaveBeenCalledWith(
      "/api/Proxy/GetAll",
      expect.objectContaining({ search: "stri", pageNumber: 0 }),
    );
    expect(proxies).toEqual([expect.objectContaining({ id: "p1", calls24h: 5, headers: [] })]);
  });

  it("gets a proxy by id via query string and maps null to null", async () => {
    logicService.get.mockResolvedValueOnce({ data: null });
    expect(await proxyService.get("missing")).toBeNull();
    expect(logicService.get).toHaveBeenCalledWith("/api/Proxy/Get?itemId=missing");
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

    expect(logicService.post).toHaveBeenCalledWith("/api/Proxy/Create", {
      name: "GitHub Proxy",
      upstream: "https://api.github.com/repos",
      methods: ["GET"],
      headers: [],
      query: [],
      bodyMerge: [],
      methodConfigs: [],
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

  it("PUTs Update and DELETEs by query string", async () => {
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
      "/api/Proxy/Update",
      expect.objectContaining({ itemId: "p1", methods: ["GET", "POST"] }),
    );

    await proxyService.delete("p1");
    expect(logicService.delete).toHaveBeenCalledWith("/api/Proxy/Delete?itemId=p1");
  });

  it("maps ProxyLogFilter to statusClass on GetExecutions", async () => {
    logicService.post.mockResolvedValueOnce({ data: [], totalCount: 0 });
    await proxyService.getExecutions("p1", "server", {});
    expect(logicService.post).toHaveBeenCalledWith(
      "/api/Proxy/GetExecutions",
      expect.objectContaining({ proxyId: "p1", statusClass: "5xx" }),
    );
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

  it("builds a CSV export result from the text body", async () => {
    logicService.get.mockResolvedValueOnce("Time,Method\n2026,GET\n2026,POST\n");
    const csv = await proxyService.exportExecutionsCsv({ proxyId: "p1", filter: "ok" });
    expect(logicService.get).toHaveBeenCalledWith(
      "/api/Proxy/ExportExecutionsCsv?proxyId=p1&statusClass=2xx",
    );
    expect(csv).toMatchObject({ fileName: "proxy-p1-executions.csv", rowCount: 2 });
  });
});
