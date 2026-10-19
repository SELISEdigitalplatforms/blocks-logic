import { beforeEach, describe, expect, it, vi } from "vitest";

const http = vi.hoisted(() => {
  const make = () => ({
    get: vi.fn().mockResolvedValue({ data: null }),
    post: vi.fn().mockResolvedValue({ data: null }),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
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
import { mapProxyDetailDtoToProxy, mapProxyListItemDtoToProxy } from "../mappers";
import { exportFileName, readImportFile } from "../utils";
import { ProxyDetailDto, ProxyListItemDto } from "../types";

const { logicService } = http;
const FILE = {
  format: "blocks.proxy.export",
  schemaVersion: 1,
  proxy: { itemId: "a" },
  contentHash: "h",
};

beforeEach(() => {
  vi.clearAllMocks();
});

describe("ProxyService export / import wiring", () => {
  it("exports with GET on the proxy's export path", async () => {
    logicService.get.mockResolvedValueOnce(FILE);
    await expect(proxyService.exportProxy("p 1")).resolves.toEqual(FILE);
    expect(logicService.get).toHaveBeenCalledWith(expect.stringMatching(/Proxies\/p%201\/export$/));
  });

  it("previews by POSTing the file unchanged and maps the answer", async () => {
    logicService.post.mockResolvedValueOnce({
      data: {
        status: "changed",
        itemId: "a",
        slug: "s",
        name: "N",
        currentVersion: 4,
        changes: [{ field: "name", label: "name", mine: "x", incoming: null }],
        missingSecrets: ["k"],
      },
    });

    const outcome = await proxyService.previewImport(FILE);

    expect(logicService.post).toHaveBeenCalledWith(
      expect.stringMatching(/Proxies\/import\/preview$/),
      FILE,
    );
    expect(outcome).toMatchObject({
      ok: true,
      result: {
        status: "changed",
        currentVersion: 4,
        changes: [{ field: "name", mine: "x", incoming: null }],
        missingSecrets: ["k"],
        duplicateSecrets: [],
      },
    });
  });

  it("applies with the resolution, choices and expected version", async () => {
    logicService.post.mockResolvedValueOnce({
      data: { status: "new", itemId: "a", slug: "s", name: "N", applied: true },
    });

    const outcome = await proxyService.applyImport({
      file: FILE,
      resolution: "merge",
      fieldChoices: { name: "incoming" },
      expectedVersion: 4,
    });

    expect(logicService.post).toHaveBeenCalledWith(
      expect.stringMatching(/Proxies\/import\/apply$/),
      {
        file: FILE,
        resolution: "merge",
        fieldChoices: { name: "incoming" },
        expectedVersion: 4,
      },
    );
    expect(outcome).toMatchObject({ ok: true, result: { applied: true } });
  });

  it("keeps the analysis of a blocked (409) import so the reason can be shown", async () => {
    logicService.post.mockRejectedValueOnce(
      new FakeHttpError(409, {
        code: "PROXY_IMPORT_BLOCKED",
        message: "Another proxy here already uses the address 's'.",
        data: { status: "blocked", itemId: "a", slug: "s", name: "N", reasonCode: "slugConflict" },
      }),
    );

    const outcome = await proxyService.applyImport({ file: FILE });

    expect(outcome).toMatchObject({
      ok: false,
      status: 409,
      code: "PROXY_IMPORT_BLOCKED",
      result: { status: "blocked", reasonCode: "slugConflict" },
    });
  });

  it("turns a refused file into plain messages", async () => {
    logicService.post.mockRejectedValueOnce(
      new FakeHttpError(400, {
        code: "PROXY_VALIDATION",
        message: "The proxy configuration in this file is invalid.",
        errors: { upstream: "The upstream must use https." },
      }),
    );

    const outcome = await proxyService.previewImport(FILE);

    expect(outcome).toEqual({
      ok: false,
      status: 400,
      code: "PROXY_VALIDATION",
      message: "The proxy configuration in this file is invalid.",
      errors: ["The upstream must use https."],
      result: null,
    });
  });

  it("reads an unknown status as blocked, never as safe", async () => {
    logicService.post.mockResolvedValueOnce({
      data: { status: "weird", itemId: "a", slug: "s", name: "N" },
    });
    const outcome = await proxyService.previewImport(FILE);
    expect(outcome).toMatchObject({ ok: true, result: { status: "blocked" } });
  });
});

describe("needs-secret fields", () => {
  it("maps missing / duplicate secrets on the detail read and the hint on the list row", () => {
    const detail = mapProxyDetailDtoToProxy({
      itemId: "p",
      name: "P",
      slug: "p",
      path: "",
      upstream: "https://x.test",
      upstreamMasked: "",
      methods: ["GET"],
      enabled: true,
      headers: [],
      query: [],
      methodConfigs: [],
      currentVersion: 1,
      createdDate: "",
      lastUpdatedDate: "",
      missingSecrets: ["a", "b"],
      duplicateSecrets: ["b"],
    } as ProxyDetailDto);
    expect(detail.missingSecrets).toEqual(["a", "b"]);
    expect(detail.duplicateSecrets).toEqual(["b"]);

    const older = mapProxyDetailDtoToProxy({
      ...({} as ProxyDetailDto),
      methods: [],
      headers: [],
      query: [],
      methodConfigs: [],
    });
    expect(older.missingSecrets).toEqual([]);

    const row = mapProxyListItemDtoToProxy({
      hasUnboundSecrets: true,
      methods: [],
    } as unknown as ProxyListItemDto);
    expect(row.hasUnboundSecrets).toBe(true);
  });
});

describe("import file helpers", () => {
  it("names the export after the slug", () => {
    expect(exportFileName("stripe-payments")).toBe("proxy-stripe-payments.json");
  });

  it("reads a proxy export and refuses anything else", async () => {
    const file = (text: string, size?: number) => {
      const f = new File([text], "f.json");
      if (size) Object.defineProperty(f, "size", { value: size });
      return f;
    };

    await expect(readImportFile(file(JSON.stringify(FILE)))).resolves.toEqual({
      ok: true,
      file: FILE,
    });
    await expect(readImportFile(file("{"))).resolves.toEqual({
      ok: false,
      error: "This file is not valid JSON.",
    });
    await expect(readImportFile(file('{"format":"x"}'))).resolves.toMatchObject({ ok: false });
    await expect(readImportFile(file("null"))).resolves.toMatchObject({ ok: false });
    await expect(readImportFile(file("{}", 2 * 1024 * 1024))).resolves.toEqual({
      ok: false,
      error: "This file is too large to be a proxy export.",
    });
  });
});
