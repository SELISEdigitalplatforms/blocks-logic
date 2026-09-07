import { beforeEach, describe, expect, it } from "vitest";
import { proxyService } from "./proxy.service";

describe("proxy service mock store", () => {
  beforeEach(() => {
    proxyService.resetMockStore();
  });

  it("creates, updates, toggles, and deletes proxies through the service boundary", async () => {
    const create = await proxyService.create({
      name: "GitHub Proxy",
      upstreamUrl: "https://api.github.com/repos",
      methods: ["GET"],
      headers: [],
      query: [],
    });

    expect(create.isSuccess).toBe(true);
    expect(create.data?.slug).toBe("github-proxy");

    const update = await proxyService.update({
      id: create.itemId!,
      values: {
        name: "GitHub Repository Proxy",
        upstreamUrl: "https://api.github.com/search/repositories",
        methods: ["GET", "POST"],
        headers: [{ key: "Authorization", value: "${SECRET.GITHUB}", isSecretRef: true }],
        query: [],
      },
    });
    expect(update.data?.methods).toEqual(["GET", "POST"]);

    const toggle = await proxyService.toggle({ id: create.itemId!, enabled: false });
    expect(toggle.data?.enabled).toBe(false);

    const remove = await proxyService.delete(create.itemId!);
    expect(remove.isSuccess).toBe(true);
    expect(await proxyService.get(create.itemId!)).toBeNull();
  });

  it("filters logs, exports CSV, and returns typed test responses", async () => {
    const serverLogs = await proxyService.getExecutions("p1", "server");
    expect(serverLogs).toHaveLength(1);
    expect(serverLogs[0].status).toBe(502);

    const csv = await proxyService.exportExecutionsCsv({ proxyId: "p1", filter: "ok" });
    expect(csv.fileName).toBe("proxy-stripe-payments-executions.csv");
    expect(csv.rowCount).toBe(1);
    expect(csv.csv).toContain("TIME,METH,PATH,CODE,TOOK,UPSTREAM");

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
    ).resolves.toMatchObject({ ok: false, status: 502 });
  });

  it("leaves state unchanged when reverting a missing version", async () => {
    const before = await proxyService.get("p1");
    const result = await proxyService.revert({ proxyId: "p1", versionId: "missing" });
    const after = await proxyService.get("p1");

    expect(result).toMatchObject({ isSuccess: false });
    expect(after).toEqual(before);
  });
});

