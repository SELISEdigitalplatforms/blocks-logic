import { createElement, StrictMode } from "react";
import { act, render, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Monaco } from "@monaco-editor/react";

// A stand-in for `loader.init()` with `makeCancelable`'s semantics from @monaco-editor/loader:
// `cancel()` only flags the promise, which rejects with the cancellation once the load settles.
const fake = vi.hoisted(() => {
  const CANCELATION = { type: "cancelation", msg: "operation is manually canceled" };
  type Load = {
    resolve: (monaco: unknown) => void;
    /** Resolves even after `cancel()`: a load whose result was already on its way. */
    resolveRaw: (monaco: unknown) => void;
    reject: (reason: unknown) => void;
    cancel: () => void;
  };
  const loads: Load[] = [];
  const state = { cached: null as unknown };
  const init = vi.fn(() => {
    let canceled = false;
    const load = {} as Load;
    const promise = new Promise((resolve, reject) => {
      load.resolve = (monaco) => (canceled ? reject(CANCELATION) : resolve(monaco));
      load.resolveRaw = resolve;
      load.reject = reject;
    }) as Promise<unknown> & { cancel: () => void };
    load.cancel = vi.fn(() => {
      canceled = true;
    });
    promise.cancel = load.cancel;
    loads.push(load);
    return promise;
  });
  return { loads, state, init, rollbarError: vi.fn() };
});

vi.mock("@monaco-editor/react", () => ({
  loader: {
    config: vi.fn(),
    init: fake.init,
    __getMonacoInstance: () => fake.state.cached,
  },
}));

const rollbar = vi.hoisted(() => ({ instance: {} as Record<string, unknown> }));
vi.mock("@seliseblocks/genesis-os/observability", () => ({ getRollbar: () => rollbar.instance }));

import { loader } from "@monaco-editor/react";
import { MONACO_VS_PATH, useMonacoInstance } from "./monaco-loader";

const MONACO = { editor: {} } as unknown as Monaco;

// Node reports unhandled rejections on `process`, jsdom on `window`; listen on both.
let unhandled: unknown[];
const onProcessRejection = (reason: unknown) => unhandled.push(reason);
const onWindowRejection = (event: PromiseRejectionEvent) => unhandled.push(event.reason);
let consoleError: ReturnType<typeof vi.spyOn>;

// Lets settled promises run their handlers and Node report whatever stayed unhandled.
const flush = () => act(() => new Promise((resolve) => setTimeout(resolve, 0)));

beforeEach(() => {
  fake.loads.length = 0;
  fake.state.cached = null;
  fake.init.mockClear();
  fake.rollbarError.mockReset();
  rollbar.instance = { error: fake.rollbarError };
  unhandled = [];
  process.on("unhandledRejection", onProcessRejection);
  window.addEventListener("unhandledrejection", onWindowRejection);
  consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);
});

afterEach(() => {
  process.off("unhandledRejection", onProcessRejection);
  window.removeEventListener("unhandledrejection", onWindowRejection);
  consoleError.mockRestore();
});

describe("monaco loader config", () => {
  it("loads Monaco's AMD build from our own origin", () => {
    expect(MONACO_VS_PATH).toBe("/monaco/vs");
    expect(loader.config).toHaveBeenCalledWith({ paths: { vs: "/monaco/vs" } });
  });
});

describe("useMonacoInstance", () => {
  it("returns a cached instance on the first render without loading again (H7)", () => {
    fake.state.cached = MONACO;
    const { result } = renderHook(() => useMonacoInstance());

    expect(result.current).toBe(MONACO);
    expect(fake.init).not.toHaveBeenCalled();
  });

  it("loads once and returns the resolved instance (H9)", async () => {
    const { result, rerender } = renderHook(() => useMonacoInstance());
    expect(result.current).toBeNull();
    expect(fake.init).toHaveBeenCalledTimes(1);

    // Re-renders while loading reuse the pending load.
    rerender();
    expect(fake.init).toHaveBeenCalledTimes(1);

    await act(async () => fake.loads[0].resolve(MONACO));
    expect(result.current).toBe(MONACO);
    expect(fake.init).toHaveBeenCalledTimes(1);
  });

  it("cancels on unmount with no unhandled rejection and no error (H8, C5)", async () => {
    const { result, unmount } = renderHook(() => useMonacoInstance());
    unmount();
    expect(fake.loads[0].cancel).toHaveBeenCalledTimes(1);

    // The load finishing after the unmount rejects with the cancellation, which is ignored.
    fake.loads[0].resolve(MONACO);
    await flush();

    expect(result.current).toBeNull();
    expect(unhandled).toEqual([]);
    expect(consoleError).not.toHaveBeenCalled();
    expect(fake.rollbarError).not.toHaveBeenCalled();
  });

  it("ignores a load that resolves after its effect was cleaned up (C5)", async () => {
    // Under <StrictMode> the first effect run is cleaned up while the component stays mounted.
    // If that stale load still resolved (here: raw, past the cancel check) with another instance
    // after the live one, it must not overwrite the state.
    const seen: (Monaco | null)[] = [];
    const Probe = () => {
      seen.push(useMonacoInstance());
      return null;
    };
    render(createElement(StrictMode, null, createElement(Probe)));
    expect(fake.loads).toHaveLength(2);

    const stale = { editor: { stale: true } } as unknown as Monaco;
    await act(async () => fake.loads[1].resolve(MONACO));
    await act(async () => fake.loads[0].resolveRaw(stale));
    await flush();

    expect(seen.at(-1)).toBe(MONACO);
    expect(seen).not.toContain(stale);
    expect(consoleError).not.toHaveBeenCalled();
    expect(unhandled).toEqual([]);
  });

  it("does not report anything when a load resolves after the unmount (C5)", async () => {
    const { unmount } = renderHook(() => useMonacoInstance());
    unmount();
    await act(async () => fake.loads[0].resolveRaw(MONACO));
    await flush();

    expect(consoleError).not.toHaveBeenCalled();
    expect(unhandled).toEqual([]);
  });

  it("ends with a loaded instance under Strict Mode's double mount (C4)", async () => {
    // A component under <StrictMode> (renderHook's wrapper does not replay effects).
    const seen: (Monaco | null)[] = [];
    const Probe = () => {
      seen.push(useMonacoInstance());
      return null;
    };
    render(createElement(StrictMode, null, createElement(Probe)));
    // Strict Mode runs the effect, its cleanup, then the effect again: two loads, the first canceled.
    expect(fake.loads).toHaveLength(2);
    expect(fake.loads[0].cancel).toHaveBeenCalledTimes(1);

    await act(async () => {
      fake.loads.forEach((load) => load.resolve(MONACO));
    });
    await flush();

    expect(seen.at(-1)).toBe(MONACO);
    expect(unhandled).toEqual([]);
    expect(consoleError).not.toHaveBeenCalled();
  });

  it("reports a real load failure to the console and Rollbar once (C1)", async () => {
    const failure = new Error("Failed to load /monaco/vs/loader.js");
    const { result } = renderHook(() => useMonacoInstance());

    await act(async () => fake.loads[0].reject(failure));
    await flush();

    expect(result.current).toBeNull();
    expect(consoleError).toHaveBeenCalledTimes(1);
    expect(consoleError).toHaveBeenCalledWith("Monaco initialization: error:", failure);
    expect(fake.rollbarError).toHaveBeenCalledTimes(1);
    expect(fake.rollbarError).toHaveBeenCalledWith("Monaco initialization failed", failure);
    expect(unhandled).toEqual([]);
  });

  it("still reports a real failure that arrives after the unmount", async () => {
    const failure = new Error("network");
    const { unmount } = renderHook(() => useMonacoInstance());
    unmount();

    fake.loads[0].reject(failure);
    await flush();

    expect(consoleError).toHaveBeenCalledWith("Monaco initialization: error:", failure);
    expect(fake.rollbarError).toHaveBeenCalledTimes(1);
    expect(unhandled).toEqual([]);
  });

  it.each([
    ["null", null, "null"],
    ["undefined", undefined, "undefined"],
    ["a string", "script error", "script error"],
    ["a number", 0, "0"],
    ["an object of another type", { type: "network" }, { type: "network" }],
  ])("treats %s as a real failure without throwing (C2)", async (_label, reason, logged) => {
    renderHook(() => useMonacoInstance());

    await act(async () => fake.loads[0].reject(reason));
    await flush();

    expect(consoleError).toHaveBeenCalledWith("Monaco initialization: error:", reason);
    expect(fake.rollbarError).toHaveBeenCalledWith("Monaco initialization failed", logged);
    expect(unhandled).toEqual([]);
  });

  it("keeps working when the Rollbar client cannot report (C10)", async () => {
    // The shared test stub's getRollbar returns `{}`: no `error` method at all.
    rollbar.instance = {};
    const { result } = renderHook(() => useMonacoInstance());

    await act(async () => fake.loads[0].reject(new Error("boom")));
    await flush();

    expect(result.current).toBeNull();
    expect(consoleError).toHaveBeenCalledTimes(1);
    expect(unhandled).toEqual([]);
  });

  it("keeps working when console.error itself throws (C10)", async () => {
    consoleError.mockImplementation(() => {
      throw new Error("console patched");
    });
    renderHook(() => useMonacoInstance());

    await act(async () => fake.loads[0].reject(new Error("boom")));
    await flush();

    expect(unhandled).toEqual([]);
  });

  it("keeps working when the Rollbar client throws (C10)", async () => {
    rollbar.instance = {
      error: () => {
        throw new Error("rollbar down");
      },
    };
    renderHook(() => useMonacoInstance());

    await act(async () => fake.loads[0].reject(new Error("boom")));
    await flush();

    expect(consoleError).toHaveBeenCalledTimes(1);
    expect(unhandled).toEqual([]);
  });
});
