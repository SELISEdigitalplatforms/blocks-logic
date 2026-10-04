import { beforeAll, describe, expect, it, vi } from "vitest";
import { createRef } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { ResizeHandle } from "./resize-handle";

// jsdom has no pointer capture.
beforeAll(() => {
  Element.prototype.setPointerCapture ??= () => {};
  Element.prototype.releasePointerCapture ??= () => {};
  Element.prototype.hasPointerCapture ??= () => false;
});

const setup = (renderedHeight = 500, height: number | null = null) => {
  const target = document.createElement("div");
  target.getBoundingClientRect = () => ({ height: renderedHeight }) as DOMRect;
  const targetRef = createRef<HTMLElement>() as { current: HTMLElement | null };
  targetRef.current = target;
  const onResize = vi.fn();
  const onReset = vi.fn();
  render(
    <ResizeHandle
      targetRef={targetRef}
      height={height}
      min={400}
      max={1600}
      onResize={onResize}
      onReset={onReset}
      label="Resize code editor"
    />,
  );
  return {
    handle: screen.getByRole("separator", { name: "Resize code editor" }),
    onResize,
    onReset,
  };
};

describe("ResizeHandle", () => {
  it("grows and shrinks with a drag, starting from the rendered height", () => {
    const { handle, onResize } = setup(500);
    fireEvent.pointerDown(handle, { button: 0, pointerId: 1, clientY: 100 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientY: 250 });
    expect(onResize).toHaveBeenLastCalledWith(650);
    fireEvent.pointerMove(handle, { pointerId: 1, clientY: 50 });
    expect(onResize).toHaveBeenLastCalledWith(450);
  });

  it("never goes past the min or the max", () => {
    const { handle, onResize } = setup(500);
    fireEvent.pointerDown(handle, { button: 0, pointerId: 1, clientY: 100 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientY: 5000 });
    expect(onResize).toHaveBeenLastCalledWith(1600);
    fireEvent.pointerMove(handle, { pointerId: 1, clientY: -5000 });
    expect(onResize).toHaveBeenLastCalledWith(400);
  });

  it("stops following the pointer once released, and ignores other buttons and pointers", () => {
    const { handle, onResize } = setup(500);
    fireEvent.pointerDown(handle, { button: 2, pointerId: 1, clientY: 100 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientY: 200 });
    expect(onResize).not.toHaveBeenCalled();

    fireEvent.pointerDown(handle, { button: 0, pointerId: 1, clientY: 100 });
    fireEvent.pointerMove(handle, { pointerId: 2, clientY: 300 });
    expect(onResize).not.toHaveBeenCalled();
    fireEvent.pointerUp(handle, { pointerId: 1 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientY: 300 });
    expect(onResize).not.toHaveBeenCalled();
  });

  it("resizes with the keyboard", () => {
    const { handle, onResize } = setup(500);
    fireEvent.keyDown(handle, { key: "ArrowDown" });
    expect(onResize).toHaveBeenLastCalledWith(520);
    fireEvent.keyDown(handle, { key: "ArrowUp", shiftKey: true });
    expect(onResize).toHaveBeenLastCalledWith(400);
    fireEvent.keyDown(handle, { key: "End" });
    expect(onResize).toHaveBeenLastCalledWith(1600);
    fireEvent.keyDown(handle, { key: "Home" });
    expect(onResize).toHaveBeenLastCalledWith(400);
    fireEvent.keyDown(handle, { key: "a" });
    expect(onResize).toHaveBeenCalledTimes(4);
  });

  it("resets on double-click", () => {
    const { handle, onReset } = setup(800, 800);
    fireEvent.doubleClick(handle);
    expect(onReset).toHaveBeenCalledOnce();
    expect(handle.getAttribute("aria-valuenow")).toBe("800");
  });
});
