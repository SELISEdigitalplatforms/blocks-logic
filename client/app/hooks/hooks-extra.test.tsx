import React from "react";
import { act, renderHook, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useFilteredMenus } from "./use-filtered-menus";
import { useActiveFiltersCount } from "./use-active-filters-count";
import { useCopyToClipboard } from "./use-copy-to-clipboard";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const menu = (id: string, extra: Record<string, unknown> = {}): any => ({
  id,
  type: "item",
  label: id,
  ...extra,
});

const wrapperAt = (path: string) => {
  const Wrapper = ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={[path]}>{children}</MemoryRouter>
  );
  return Wrapper;
};

describe("useFilteredMenus", () => {
  it("hides disabled items and keeps enabled ones", () => {
    const { result } = renderHook(
      () =>
        useFilteredMenus([
          menu("a"),
          menu("b", { disabled: true }),
        ]),
      { wrapper: wrapperAt("/") },
    );
    expect(result.current.map((m) => m.id)).toEqual(["a"]);
  });

  it("hides project-overview menus when not on that route", () => {
    const { result } = renderHook(
      () => useFilteredMenus([menu("people"), menu("a")]),
      { wrapper: wrapperAt("/") },
    );
    expect(result.current.map((m) => m.id)).toEqual(["a"]);
  });

  it("hides non-project menus when on the project-overview route", () => {
    const { result } = renderHook(
      () => useFilteredMenus([menu("service-workflow"), menu("people")]),
      { wrapper: wrapperAt("/project-overview") },
    );
    expect(result.current.map((m) => m.id)).toEqual(["people"]);
  });

  it("drops separators that are adjacent to other separators or at the edges", () => {
    const { result } = renderHook(
      () =>
        useFilteredMenus([
          menu("sep1", { type: "separator" }),
          menu("a"),
          menu("sep2", { type: "separator" }),
          menu("b"),
        ]),
      { wrapper: wrapperAt("/") },
    );
    const ids = result.current.map((m) => m.id);
    // leading separator dropped, the interior one between two items kept
    expect(ids).toContain("a");
    expect(ids).toContain("b");
    expect(ids).not.toContain("sep1");
  });
});

describe("useActiveFiltersCount", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const makeTable = (columnFilters: any[]): any => ({
    getState: () => ({ columnFilters }),
  });

  it("counts array, object, primitive and search-type filters", () => {
    const table = makeTable([
      { id: "search", value: { types: ["a", "b"] } },
      { id: "tags", value: ["x", "y", "z"] },
      { id: "meta", value: { k1: 1, k2: 2 } },
      { id: "name", value: "john" },
      { id: "empty", value: "" },
    ]);
    const { result } = renderHook(() =>
      useActiveFiltersCount(table, undefined, "search"),
    );
    // 2 (search types) + 3 (array) + 2 (object keys) + 1 (primitive) = 8
    expect(result.current).toBe(8);
  });

  it("adds one when a date range is present", () => {
    const table = makeTable([]);
    const { result } = renderHook(() =>
      useActiveFiltersCount(table, { from: new Date(), to: undefined }, "search"),
    );
    expect(result.current).toBe(1);
  });
});

describe("useCopyToClipboard", () => {
  afterEach(() => vi.restoreAllMocks());

  it("copies text and calls the success callback", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.assign(navigator, { clipboard: { writeText } });
    const onSuccess = vi.fn();
    const { result } = renderHook(() => useCopyToClipboard());
    await act(async () => {
      await result.current.copy("hello", onSuccess);
    });
    expect(writeText).toHaveBeenCalledWith("hello");
    expect(onSuccess).toHaveBeenCalled();
  });

  it("reports an error when the clipboard API is missing", async () => {
    Object.assign(navigator, { clipboard: undefined });
    const onError = vi.fn();
    const { result } = renderHook(() => useCopyToClipboard());
    await act(async () => {
      await result.current.copy("x", undefined, onError);
    });
    expect(onError).toHaveBeenCalledWith(expect.any(Error));
  });

  it("reports an error when writeText rejects", async () => {
    const writeText = vi.fn().mockRejectedValue(new Error("denied"));
    Object.assign(navigator, { clipboard: { writeText } });
    const onError = vi.fn();
    const { result } = renderHook(() => useCopyToClipboard());
    await act(async () => {
      await result.current.copy("x", undefined, onError);
    });
    await waitFor(() => expect(onError).toHaveBeenCalledWith(expect.any(Error)));
  });
});
