import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { makeHookWrapper } from "@/test-utils/test-providers/render";

vi.mock("@/services/secret.service", async () => {
  const actual = await import("@/services/secret.service");
  const { mockSecretService } = await import("../test-support/mock-secret-service");
  return { ...actual, secretService: mockSecretService };
});

import { useSecrets } from "./use-secrets";

describe("useSecrets", () => {
  it("returns the tenant's platform secrets", async () => {
    const wrapper = makeHookWrapper();
    const { result } = renderHook(() => useSecrets(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect((result.current.data ?? []).map((v) => v.name)).toEqual([
      "stripe-api-key",
      "sendgrid-api-key",
    ]);
  });

  it("passes the search filter through to the service", async () => {
    const wrapper = makeHookWrapper();
    const { result } = renderHook(() => useSecrets({ search: "stripe" }), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect((result.current.data ?? []).map((v) => v.name)).toEqual(["stripe-api-key"]);
  });
});
