import { beforeEach, describe, expect, it, vi } from "vitest";

const http = vi.hoisted(() => ({
  logicService: {
    get: vi.fn().mockResolvedValue({ ok: true }),
    post: vi.fn().mockResolvedValue({ ok: true }),
    put: vi.fn().mockResolvedValue({ ok: true }),
    delete: vi.fn().mockResolvedValue({ ok: true }),
  },
}));

vi.mock("@/lib/http-client", () => ({ serviceInstances: http }));

import { functionService } from "./function.service";

beforeEach(() => {
  vi.clearAllMocks();
});

describe("functionService", () => {
  it("posts to GetAll for getFunctions", async () => {
    await functionService.getFunctions({ searchKey: "x", pageNumber: 0, pageSize: 10 });
    expect(http.logicService.post).toHaveBeenCalledWith(
      expect.stringContaining("/Functions/GetAll"),
      { searchKey: "x", pageNumber: 0, pageSize: 10 },
    );
  });

  it("gets a function by id via query string", async () => {
    await functionService.getFunction("fn_1");
    expect(http.logicService.get).toHaveBeenCalledWith(expect.stringContaining("functionId=fn_1"));
  });

  it("posts Create/Update/Save with the payload verbatim", async () => {
    await functionService.createFunction({ name: "n" });
    expect(http.logicService.post).toHaveBeenCalledWith(
      expect.stringContaining("/Functions/Create"),
      { name: "n" },
    );

    await functionService.saveFunction({
      functionId: "fn_1",
      indexJs: "",
      packageJson: "",
      limits: {} as never,
      retry: {} as never,
      trigger: {} as never,
      outputActions: [],
      variables: [],
    });
    expect(http.logicService.post).toHaveBeenCalledWith(
      expect.stringContaining("/Functions/Save"),
      expect.objectContaining({ functionId: "fn_1" }),
    );
  });

  it("deletes via query string, not a body", async () => {
    await functionService.deleteFunction("fn_1");
    expect(http.logicService.delete).toHaveBeenCalledWith(
      expect.stringContaining("functionId=fn_1"),
    );
  });

  it("tests a function against the Test endpoint", async () => {
    await functionService.testFunction({ functionId: "fn_1", inputJson: "{}" });
    expect(http.logicService.post).toHaveBeenCalledWith(
      expect.stringContaining("/Functions/Test"),
      { functionId: "fn_1", inputJson: "{}" },
    );
  });

  it("deploys and rolls back by function id", async () => {
    await functionService.deployFunction({ functionId: "fn_1" });
    expect(http.logicService.post).toHaveBeenCalledWith(
      expect.stringContaining("/Functions/Deploy"),
      { functionId: "fn_1" },
    );

    await functionService.rollbackFunction({ functionId: "fn_1", versionNumber: 2 });
    expect(http.logicService.post).toHaveBeenCalledWith(
      expect.stringContaining("/Functions/Rollback"),
      { functionId: "fn_1", versionNumber: 2 },
    );
  });

  it("builds GetVersions and GetVersionSource query strings", async () => {
    await functionService.getVersions("fn_1", 1, 5);
    expect(http.logicService.get).toHaveBeenCalledWith(
      expect.stringMatching(/GetVersions\?.*functionId=fn_1.*pageNumber=1.*pageSize=5/),
    );

    await functionService.getVersionSource("fn_1", "v_1");
    expect(http.logicService.get).toHaveBeenCalledWith(
      expect.stringMatching(/GetVersionSource\?.*functionId=fn_1.*versionId=v_1/),
    );
  });

  it("only includes provided filters in GetRuns", async () => {
    await functionService.getRuns({ functionId: "fn_1", pageNumber: 0, pageSize: 20 });
    const [url] = http.logicService.get.mock.calls[0];
    expect(url).toContain("functionId=fn_1");
    expect(url).not.toContain("status=");
    expect(url).not.toContain("fromUtc=");
  });

  it("replays and cancels a run by id", async () => {
    await functionService.replayRun("run_1");
    expect(http.logicService.post).toHaveBeenCalledWith(
      expect.stringContaining("/Functions/ReplayRun"),
      { runId: "run_1" },
    );

    await functionService.cancelRun("run_1");
    expect(http.logicService.post).toHaveBeenCalledWith(
      expect.stringContaining("/Functions/CancelRun"),
      { runId: "run_1" },
    );
  });

  it("gets the secret catalog with no arguments", async () => {
    await functionService.getSecretCatalog();
    expect(http.logicService.get).toHaveBeenCalledWith(
      expect.stringContaining("/Functions/GetSecretCatalog"),
    );
  });
});
