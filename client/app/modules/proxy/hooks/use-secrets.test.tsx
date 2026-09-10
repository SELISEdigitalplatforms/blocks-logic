import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { makeHookWrapper } from "@/test-utils/test-providers/render";

vi.mock("../services", async () => {
  const actual = await import("../services");
  const { mockSecretService } = await import("../test-support/mock-secret-service");
  return { ...actual, secretService: mockSecretService };
});

import { useSecrets, useSecretTags } from "./use-secrets";

describe("useSecrets", () => {
  it("returns only the variables a proxy can resolve (service / both)", async () => {
    const wrapper = makeHookWrapper();
    const { result } = renderHook(() => useSecrets(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const names = (result.current.data ?? []).map((v) => v.name);
    expect(names).toEqual(["stripe-api-key", "sendgrid-api-key"]);
    expect(names).not.toContain("internal-only");
  });

  it("exposes the tag catalog", async () => {
    const wrapper = makeHookWrapper();
    const { result } = renderHook(() => useSecretTags(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(["payments", "mail"]);
  });
});
