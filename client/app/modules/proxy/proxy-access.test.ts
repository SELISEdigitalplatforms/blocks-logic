import { describe, expect, it } from "vitest";
import {
  mapProxyDetailDtoToProxy,
  mapProxyToCreatePayload,
  toAccess,
  toAccessPayload,
} from "./mappers/proxy.mapper";
import { ProxyDetailDto, ProxyFormValues } from "./types";
import {
  defaultProxyAccess,
  describeProxyAccess,
  proxyFormDefaultValues,
  validateProxyAccess,
} from "./utils";

const formValues = (over: Partial<ProxyFormValues> = {}): ProxyFormValues => ({
  ...proxyFormDefaultValues,
  name: "Stripe",
  upstreamUrl: "https://api.stripe.com",
  ...over,
});

const detailDto = (access: ProxyDetailDto["access"]): ProxyDetailDto => ({
  itemId: "p1",
  name: "Stripe",
  slug: "stripe",
  path: "/api/proxy/gateway/stripe/*",
  upstream: "https://api.stripe.com",
  upstreamMasked: "https://api.stripe.com",
  methods: ["GET"],
  enabled: true,
  headers: [],
  query: [],
  methodConfigs: [],
  currentVersion: 1,
  createdDate: "2026-01-01T00:00:00Z",
  lastUpdatedDate: "2026-01-01T00:00:00Z",
  access,
});

describe("proxy access — wire mapping", () => {
  it("defaults to a Blocks token with no restriction when the block is absent", () => {
    expect(toAccess(null)).toEqual(defaultProxyAccess());
    expect(mapProxyDetailDtoToProxy(detailDto(undefined)).access.kind).toBe("blocksToken");
  });

  it("translates the server vocabulary in both directions", () => {
    const access = toAccess({
      kind: "Public",
      combine: "And",
      roles: { mode: "ALL", values: ["admin"] },
      permissions: { mode: null, values: null },
    });
    expect(access).toEqual({
      kind: "public",
      combine: "and",
      roles: { mode: "all", values: ["admin"] },
      permissions: { mode: "any", values: [] },
      organizationId: "",
    });

    const payload = toAccessPayload({
      kind: "blocksToken",
      combine: "and",
      roles: { mode: "all", values: [" admin ", "admin", "editor"] },
      permissions: { mode: "any", values: ["proxy:call"] },
    });
    expect(payload).toEqual({
      kind: "BlocksToken",
      combine: "And",
      roles: { mode: "all", values: ["admin", "editor"] },
      permissions: { mode: "any", values: ["proxy:call"] },
      organizationId: "",
    });
  });

  it("drops stale chips from a public save, since the server refuses public + rules", () => {
    const payload = toAccessPayload({
      kind: "public",
      combine: "or",
      roles: { mode: "any", values: ["admin"] },
      permissions: { mode: "any", values: ["x"] },
    });
    expect(payload.kind).toBe("Public");
    expect(payload.roles).toEqual({ mode: "any", values: [] });
    expect(payload.permissions).toEqual({ mode: "any", values: [] });
  });

  it("is carried on the create payload", () => {
    const payload = mapProxyToCreatePayload(
      formValues({
        access: { ...defaultProxyAccess(), roles: { mode: "any", values: ["admin"] } },
      }),
    );
    expect(payload.access).toEqual({
      kind: "BlocksToken",
      combine: "Or",
      roles: { mode: "any", values: ["admin"] },
      permissions: { mode: "any", values: [] },
      organizationId: "",
    });
  });
});

describe("proxy access — wording and guards", () => {
  it("describes each shape of policy", () => {
    expect(describeProxyAccess({ ...defaultProxyAccess(), kind: "public" })).toMatch(
      /Anyone with the URL/,
    );
    expect(describeProxyAccess(defaultProxyAccess())).toMatch(/No extra restriction/);
    expect(
      describeProxyAccess({ ...defaultProxyAccess(), roles: { mode: "any", values: ["admin"] } }),
    ).toBe("Callers must hold the role admin.");
    expect(
      describeProxyAccess({
        ...defaultProxyAccess(),
        roles: { mode: "all", values: ["admin", "editor"] },
      }),
    ).toBe("Callers must hold all of the roles admin, editor.");
    expect(
      describeProxyAccess({
        ...defaultProxyAccess(),
        combine: "and",
        roles: { mode: "any", values: ["admin"] },
        permissions: { mode: "any", values: ["proxy:call"] },
      }),
    ).toBe("Callers must hold the role admin AND the permission proxy:call.");
  });

  it("blocks the combinations the server rejects", () => {
    expect(validateProxyAccess(defaultProxyAccess())).toBeNull();
    expect(
      validateProxyAccess({
        ...defaultProxyAccess(),
        kind: "public",
        roles: { mode: "any", values: ["a"] },
      }),
    ).toMatch(/public endpoint/);
    expect(
      validateProxyAccess({
        ...defaultProxyAccess(),
        permissions: { mode: "any", values: ["a,b"] },
      }),
    ).toMatch(/comma/);
  });
});
