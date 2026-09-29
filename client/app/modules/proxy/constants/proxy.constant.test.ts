import { beforeEach, describe, expect, it, vi } from "vitest";
import type { IProject } from "@seliseblocks/genesis-os";

const runtimeEnv = vi.hoisted(() => ({ value: "" }));
vi.mock("@seliseblocks/genesis-os", () => ({ getRuntimeEnv: () => runtimeEnv.value }));

import { getProxyPublicHost } from "./proxy.constant";

const project = (customDomain: string | null) => ({ customDomain }) as IProject;

describe("getProxyPublicHost", () => {
  beforeEach(() => {
    runtimeEnv.value = "";
  });

  it.each([
    ["acme.com", "https://blocksapi.acme.com"],
    ["https://acme.com", "https://blocksapi.acme.com"],
    ["  https://app.acme.com/some/path  ", "https://blocksapi.acme.com"],
    ["APP.Acme.COM", "https://blocksapi.acme.com"],
  ])("derives the host from the custom domain %j", (domain, host) => {
    expect(getProxyPublicHost(project(domain))).toBe(host);
  });

  it("uses the custom domain even when the public host is unset", () => {
    expect(getProxyPublicHost(project("acme.com"))).toBe("https://blocksapi.acme.com");
  });

  it.each([null, "", "   ", "localhost", "acme", "not a domain", "https://", "10.0.0.1"])(
    "falls back to the public host for an unusable domain %j",
    (domain) => {
      runtimeEnv.value = "dev-api.blocksdevelopers.com/";
      expect(getProxyPublicHost(project(domain))).toBe("https://dev-api.blocksdevelopers.com");
    },
  );

  it("keeps the public host's own scheme, and returns empty when nothing is known", () => {
    runtimeEnv.value = "http://api.local.test";
    expect(getProxyPublicHost(undefined)).toBe("http://api.local.test");
    runtimeEnv.value = "   ";
    expect(getProxyPublicHost(null)).toBe("");
  });
});
