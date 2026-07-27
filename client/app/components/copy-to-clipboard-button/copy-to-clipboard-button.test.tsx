import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { CopyToClipboardButton } from "./copy-to-clipboard-button";

describe("CopyToClipboardButton", () => {
  afterEach(() => vi.restoreAllMocks());

  it("renders children and the copy control", () => {
    render(
      <CopyToClipboardButton textToCopy="hello">
        <span>Label</span>
      </CopyToClipboardButton>,
    );
    expect(screen.getByText("Label")).toBeTruthy();
    expect(screen.getByRole("button")).toBeTruthy();
  });

  it("copies via the clipboard API in a secure context", () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.assign(navigator, { clipboard: { writeText } });
    Object.defineProperty(window, "isSecureContext", {
      value: true,
      configurable: true,
    });
    render(
      <CopyToClipboardButton textToCopy="copied-value">
        <span>x</span>
      </CopyToClipboardButton>,
    );
    fireEvent.click(screen.getByRole("button"));
    expect(writeText).toHaveBeenCalledWith("copied-value");
  });

  it("falls back to execCommand when not in a secure context", () => {
    Object.defineProperty(window, "isSecureContext", {
      value: false,
      configurable: true,
    });
    Object.assign(navigator, { clipboard: undefined });
    const exec = vi.fn();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (document as any).execCommand = exec;
    render(
      <CopyToClipboardButton textToCopy="legacy" isHoverable>
        <span>x</span>
      </CopyToClipboardButton>,
    );
    fireEvent.click(screen.getByRole("button"));
    expect(exec).toHaveBeenCalledWith("copy");
  });
});
