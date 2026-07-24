import { afterEach } from "vitest";
import { cleanup } from "@testing-library/react";

// Vitest is configured with globals: false, so @testing-library/react's
// automatic afterEach cleanup does not register itself. Wire it up manually
// so component/hook renders are torn down between tests.
afterEach(() => {
  cleanup();
});

// Some third-party ESM deps (e.g. framer-motion's motion-utils, pulled in via
// @seliseblocks/blocks-kit) read `process.env.NODE_ENV` at import time. Under
// the jsdom environment `process.env` can be undefined, which crashes module
// evaluation. Ensure a minimal, non-production process.env is always present.
{
  const g = globalThis as unknown as {
    process?: { env?: Record<string, string> };
  };
  if (!g.process) {
    g.process = { env: { NODE_ENV: "test" } };
  } else if (!g.process.env) {
    g.process.env = { NODE_ENV: "test" };
  } else if (g.process.env.NODE_ENV === undefined) {
    g.process.env.NODE_ENV = "test";
  }
}

// jsdom polyfills for Radix UI / cmdk / xyflow primitives.
if (typeof globalThis.ResizeObserver === "undefined") {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

if (typeof globalThis.IntersectionObserver === "undefined") {
  globalThis.IntersectionObserver = class {
    root = null;
    rootMargin = "";
    thresholds = [];
    observe() {}
    unobserve() {}
    disconnect() {}
    takeRecords() {
      return [];
    }
  } as unknown as typeof IntersectionObserver;
}

if (typeof Element !== "undefined") {
  if (!Element.prototype.scrollIntoView) {
    Element.prototype.scrollIntoView = () => {};
  }
  if (!Element.prototype.scrollTo) {
    Element.prototype.scrollTo = () => {};
  }
  if (!Element.prototype.hasPointerCapture) {
    Element.prototype.hasPointerCapture = () => false;
  }
  if (!Element.prototype.setPointerCapture) {
    Element.prototype.setPointerCapture = () => {};
  }
  if (!Element.prototype.releasePointerCapture) {
    Element.prototype.releasePointerCapture = () => {};
  }
}

if (typeof globalThis.matchMedia === "undefined") {
  globalThis.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  })) as unknown as typeof matchMedia;
}
