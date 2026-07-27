import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useWorkflowFilterQueryParams } from "./use-workflow-filter-query-params";

// nuqs v1 has no built-in test adapter. We mock the module so that
// useQueryStates behaves like a plain React.useState — sufficient for
// verifying the hook's default values and setter contract.
vi.mock("nuqs", async () => {
  const { useState } = await vi.importActual<typeof import("react")>("react");
  return {
    parseAsString: { withDefault: (v: string) => v },
    parseAsInteger: { withDefault: (v: number) => v },
    useQueryStates: vi.fn((schema: Record<string, unknown>) => {
      const defaults = Object.fromEntries(
        Object.entries(schema).map(([key, value]) => [key, value]),
      );
      const [state, setState] = useState(defaults);
      return [state, setState];
    }),
  };
});

describe("useWorkflowFilterQueryParams", () => {
  beforeEach(() => vi.clearAllMocks());

  it("should initialise with the correct default values", () => {
    const { result } = renderHook(() => useWorkflowFilterQueryParams());

    expect(result.current.queryParams.search).toBe("");
    expect(result.current.queryParams.isPublished).toBe("all");
    expect(result.current.queryParams.page).toBe(0);
    expect(result.current.queryParams.pageSize).toBe(10);
  });

  it("should expose a setQueryParams function", () => {
    const { result } = renderHook(() => useWorkflowFilterQueryParams());
    expect(typeof result.current.setQueryParams).toBe("function");
  });

  it("should update search when setQueryParams is called", () => {
    const { result } = renderHook(() => useWorkflowFilterQueryParams());

    act(() => {
      result.current.setQueryParams((prev: typeof result.current.queryParams) => ({
        ...prev,
        search: "automation",
      }));
    });

    expect(result.current.queryParams.search).toBe("automation");
  });

  it("should update page when setQueryParams is called", () => {
    const { result } = renderHook(() => useWorkflowFilterQueryParams());

    act(() => {
      result.current.setQueryParams((prev: typeof result.current.queryParams) => ({
        ...prev,
        page: 3,
      }));
    });

    expect(result.current.queryParams.page).toBe(3);
  });

  it("should update isPublished filter when setQueryParams is called", () => {
    const { result } = renderHook(() => useWorkflowFilterQueryParams());

    act(() => {
      result.current.setQueryParams((prev: typeof result.current.queryParams) => ({
        ...prev,
        isPublished: "true",
      }));
    });

    expect(result.current.queryParams.isPublished).toBe("true");
  });
});
