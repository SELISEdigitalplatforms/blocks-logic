import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { downloadJson } from "./download-json.util";

describe("downloadJson", () => {
  const clickSpy = vi.fn();
  let createObjectURL: ReturnType<typeof vi.fn>;
  let revokeObjectURL: ReturnType<typeof vi.fn>;
  let createElement: typeof document.createElement;

  beforeEach(() => {
    clickSpy.mockClear();
    createObjectURL = vi.fn(() => "blob:mock");
    revokeObjectURL = vi.fn();
    // @ts-expect-error jsdom lacks these
    URL.createObjectURL = createObjectURL;
    // @ts-expect-error jsdom lacks these
    URL.revokeObjectURL = revokeObjectURL;
    createElement = document.createElement.bind(document);
    vi.spyOn(document, "createElement").mockImplementation((tag: string) => {
      const el = createElement(tag) as HTMLElement;
      if (tag === "a") {
        Object.defineProperty(el, "click", { value: clickSpy });
      }
      return el;
    });
  });

  afterEach(() => vi.restoreAllMocks());

  it("serialises pretty-printed JSON and triggers a download", () => {
    downloadJson("wf-2026-09-07.json", { name: "wf", nodes: [] });

    expect(createObjectURL).toHaveBeenCalledTimes(1);
    const blob = createObjectURL.mock.calls[0][0] as Blob;
    expect(blob.type).toBe("application/json");
    expect(clickSpy).toHaveBeenCalledTimes(1);
    expect(revokeObjectURL).toHaveBeenCalledWith("blob:mock");
  });

  it("sets the download filename on the anchor", () => {
    let anchor: HTMLAnchorElement | undefined;
    (document.createElement as unknown as ReturnType<typeof vi.spyOn>).mockImplementation(
      (tag: string) => {
        const el = createElement(tag);
        if (tag === "a") {
          Object.defineProperty(el, "click", { value: clickSpy });
          anchor = el as HTMLAnchorElement;
        }
        return el;
      },
    );

    downloadJson("my-file.json", { a: 1 });
    expect(anchor?.getAttribute("download")).toBe("my-file.json");
  });
});
