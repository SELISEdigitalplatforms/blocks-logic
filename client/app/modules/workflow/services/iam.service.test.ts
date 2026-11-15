import { beforeEach, describe, expect, it, vi } from "vitest";

const http = vi.hoisted(() => ({
  iamService: {
    get: vi.fn(),
    post: vi.fn(),
  },
}));

vi.mock("@/lib/http-client", () => ({ serviceInstances: http }));

import { iamService } from "./iam.service";

const TODAYS_BODY = {
  page: 0,
  pageSize: 20,
  filter: { search: "" },
  sort: { property: "Name", isDescending: false },
};

beforeEach(() => {
  http.iamService.get.mockReset().mockResolvedValue({ ok: true });
  http.iamService.post.mockReset().mockResolvedValue({ ok: true });
});

describe("iamService.getRoles organizationId", () => {
  it("H12 / Example 9: sends a non-empty organizationId as a top-level key", async () => {
    await iamService.getRoles({ search: "", page: 0, pageSize: 20, organizationId: "org1" });
    const [url, body, , options] = http.iamService.post.mock.calls[0];
    expect(url).toMatch(/\/api\/iam\/roles$/);
    expect(body).toStrictEqual({ ...TODAYS_BODY, organizationId: "org1" });
    expect(Object.keys(body as object)).toEqual(["page", "pageSize", "filter", "sort", "organizationId"]);
    expect(options).toEqual({ absoluteUrl: true });
  });

  it("C10: sends exactly today's body without an organizationId or with an empty one", async () => {
    await iamService.getRoles({ search: "", page: 0, pageSize: 20 });
    await iamService.getRoles({ search: "", page: 0, pageSize: 20, organizationId: "" });
    for (const call of http.iamService.post.mock.calls) {
      expect(call[1]).toStrictEqual(TODAYS_BODY);
      expect(call[1]).not.toHaveProperty("organizationId");
    }
  });

  it("C10: keeps the no-argument body", async () => {
    await iamService.getRoles();
    expect(http.iamService.post.mock.calls[0][1]).toStrictEqual({ ...TODAYS_BODY, pageSize: 100 });
  });
});

describe("iamService.getOrganizationConfig", () => {
  it("H12: GETs the organization config and returns the response", async () => {
    const config = { isMultiOrgEnabled: true, other: "x" };
    http.iamService.get.mockResolvedValue(config);
    await expect(iamService.getOrganizationConfig()).resolves.toBe(config);
    expect(http.iamService.get).toHaveBeenCalledTimes(1);
    const [url, query, options] = http.iamService.get.mock.calls[0];
    expect(url).toMatch(/\/api\/iam\/organizations\/config\/admin$/);
    expect(query).toBeUndefined();
    expect(options).toEqual({ absoluteUrl: true });
  });

  it("uses the same base URL as getOrganizations", async () => {
    await iamService.getOrganizations();
    await iamService.getOrganizationConfig();
    const orgsUrl = http.iamService.get.mock.calls[0][0] as string;
    const configUrl = http.iamService.get.mock.calls[1][0] as string;
    expect(configUrl.replace("/config/admin", "")).toBe(orgsUrl.split("?")[0]);
  });
});
