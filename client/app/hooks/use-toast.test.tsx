import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  reducer,
  showErrorToast,
  showInfoToast,
  showSuccessToast,
  toast,
  useToast,
} from "./use-toast";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const t = (id: string, extra: Record<string, unknown> = {}): any => ({
  id,
  open: true,
  ...extra,
});

describe("reducer", () => {
  it("ADD_TOAST prepends and enforces the toast limit", () => {
    const s1 = reducer({ toasts: [] }, { type: "ADD_TOAST", toast: t("1") });
    expect(s1.toasts).toHaveLength(1);
    const s2 = reducer(s1, { type: "ADD_TOAST", toast: t("2") });
    // TOAST_LIMIT is 1
    expect(s2.toasts).toHaveLength(1);
    expect(s2.toasts[0].id).toBe("2");
  });

  it("UPDATE_TOAST merges matching ids", () => {
    const state = { toasts: [t("1", { title: "a" })] };
    const next = reducer(state, {
      type: "UPDATE_TOAST",
      toast: { id: "1", title: "b" },
    });
    expect(next.toasts[0].title).toBe("b");
  });

  it("DISMISS_TOAST closes a specific toast", () => {
    const state = { toasts: [t("1"), t("2")] };
    const next = reducer(state, { type: "DISMISS_TOAST", toastId: "1" });
    expect(next.toasts.find((x) => x.id === "1")?.open).toBe(false);
    expect(next.toasts.find((x) => x.id === "2")?.open).toBe(true);
  });

  it("DISMISS_TOAST with no id closes all", () => {
    const state = { toasts: [t("1"), t("2")] };
    const next = reducer(state, { type: "DISMISS_TOAST" });
    expect(next.toasts.every((x) => x.open === false)).toBe(true);
  });

  it("REMOVE_TOAST removes one or all", () => {
    const state = { toasts: [t("1"), t("2")] };
    expect(
      reducer(state, { type: "REMOVE_TOAST", toastId: "1" }).toasts,
    ).toHaveLength(1);
    expect(
      reducer(state, { type: "REMOVE_TOAST", toastId: undefined }).toasts,
    ).toHaveLength(0);
  });
});

describe("toast() and useToast()", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    vi.runOnlyPendingTimers();
    vi.useRealTimers();
  });

  it("creates a toast, exposes update and dismiss, and syncs the hook", () => {
    const { result } = renderHook(() => useToast());
    let handle: ReturnType<typeof toast>;
    act(() => {
      handle = toast({ title: "Hello" });
    });
    expect(result.current.toasts[0].title).toBe("Hello");

    act(() => {
      handle.update({ ...result.current.toasts[0], title: "Updated" });
    });
    expect(result.current.toasts[0].title).toBe("Updated");

    act(() => {
      handle.dismiss();
    });
    expect(result.current.toasts[0].open).toBe(false);
  });

  it("onOpenChange(false) dismisses the toast", () => {
    const { result } = renderHook(() => useToast());
    act(() => {
      toast({ title: "Auto" });
    });
    act(() => {
      result.current.toasts[0].onOpenChange?.(false);
    });
    expect(result.current.toasts[0].open).toBe(false);
  });

  it("hook dismiss with no id dismisses all", () => {
    const { result } = renderHook(() => useToast());
    act(() => {
      toast({ title: "x" });
    });
    act(() => {
      result.current.dismiss();
    });
    expect(result.current.toasts[0].open).toBe(false);
  });
});

describe("toast variants", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    vi.runOnlyPendingTimers();
    vi.useRealTimers();
  });

  it("showSuccessToast and showInfoToast render", () => {
    const { result } = renderHook(() => useToast());
    act(() => showSuccessToast({ description: "done" }));
    expect(result.current.toasts[0].variant).toBe("success");
    act(() => showInfoToast({ description: "fyi" }));
    expect(result.current.toasts[0].variant).toBe("info");
  });

  it("showErrorToast formats a single error string", () => {
    const { result } = renderHook(() => useToast());
    act(() => showErrorToast({ errors: "boom" }));
    expect(result.current.toasts[0].variant).toBe("destructive");
    expect(result.current.toasts[0].description).toBe("boom");
  });

  it("showErrorToast renders an array of field errors", () => {
    const { result } = renderHook(() => useToast());
    act(() =>
      showErrorToast({ errors: { name: "required", email: "invalid" } }),
    );
    // multiple messages render as an array of divs
    expect(Array.isArray(result.current.toasts[0].description)).toBe(true);
  });
});
