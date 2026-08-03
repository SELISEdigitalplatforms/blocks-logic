import React from "react";
import { act, render, renderHook, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import useRoutePathSegments from "@/hooks/use-path-segments";
import { useCountDown } from "@/hooks/use-count-down";
import { useCursorTracker } from "@/modules/workflow/components/node-inspector/form-builder/utils/use-cursor-tracker";
import { useLanguageViewStore } from "@/modules/workflow/store/use-language-view-store";
import { ModuleName } from "@/constants/modules.constants";
import QueryProvider, { getQueryClient } from "@/providers/query-provider";

describe("useRoutePathSegments", () => {
  it("builds breadcrumb segments with formatted labels", () => {
    const { result } = renderHook(() => useRoutePathSegments(), {
      wrapper: ({ children }) => (
        <MemoryRouter initialEntries={["/my-app/data-gateway"]}>
          {children}
        </MemoryRouter>
      ),
    });
    expect(result.current).toEqual([
      { href: "/my-app", label: "My App" },
      { href: "/my-app/data-gateway", label: "Data Gateway" },
    ]);
  });
});

describe("useCountDown", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("counts down each second and resets", () => {
    const { result } = renderHook(() => useCountDown(3));
    expect(result.current.remainingTime).toBe(3);
    act(() => vi.advanceTimersByTime(1000));
    expect(result.current.remainingTime).toBe(2);
    act(() => result.current.reset(10));
    expect(result.current.remainingTime).toBe(10);
    act(() => result.current.reset());
    expect(result.current.remainingTime).toBe(3);
  });
});

describe("useCursorTracker", () => {
  it("tracks selection and inserts text at the cursor", () => {
    const { result } = renderHook(() => useCursorTracker());
    // simulate a selection event
    result.current.updatePosition({
      type: "select",
      currentTarget: { selectionStart: 2, selectionEnd: 2 },
    } as unknown as React.SyntheticEvent<HTMLInputElement>);
    expect(result.current.insertAtCursor("abcd", "XY")).toBe("abXYcd");
  });

  it("ignores blur events and appends when there is no selection", () => {
    const { result } = renderHook(() => useCursorTracker());
    result.current.updatePosition({
      type: "blur",
      currentTarget: { selectionStart: 1, selectionEnd: 1 },
    } as unknown as React.SyntheticEvent<HTMLInputElement>);
    expect(result.current.insertAtCursor("ab", "Z")).toBe("abZ");
  });
});

describe("useLanguageViewStore", () => {
  beforeEach(() => {
    useLanguageViewStore.getState().resetSelectedLanguages();
  });

  it("sets and toggles languages", () => {
    const s = useLanguageViewStore.getState();
    s.setSelectedLanguages(["en"]);
    expect(useLanguageViewStore.getState().selectedLanguages).toEqual(["en"]);
    s.toggleLanguage("fr");
    expect(useLanguageViewStore.getState().selectedLanguages).toContain("fr");
    s.toggleLanguage("en");
    expect(useLanguageViewStore.getState().selectedLanguages).not.toContain("en");
  });

  it("sets and toggles optional columns", () => {
    const s = useLanguageViewStore.getState();
    s.setSelectedOptionalColumns(["a"]);
    expect(useLanguageViewStore.getState().selectedOptionalColumns).toEqual(["a"]);
    s.toggleOptionalColumn("b");
    expect(useLanguageViewStore.getState().selectedOptionalColumns).toContain("b");
    s.toggleOptionalColumn("a");
    expect(useLanguageViewStore.getState().selectedOptionalColumns).not.toContain("a");
  });
});

describe("ModuleName", () => {
  it("exposes numeric module identifiers", () => {
    expect(ModuleName.Cloud).toBe(1);
    expect(ModuleName.DataGateway).toBe(11);
  });
});

describe("QueryProvider", () => {
  it("renders children and reuses a singleton client", () => {
    render(
      <QueryProvider>
        <span>child</span>
      </QueryProvider>,
    );
    expect(screen.getByText("child")).toBeTruthy();
    expect(getQueryClient()).toBe(getQueryClient());
  });
});
