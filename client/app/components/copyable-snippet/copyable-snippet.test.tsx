import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { CopyableSnippet } from "./copyable-snippet";

describe("CopyableSnippet", () => {
  afterEach(() => vi.restoreAllMocks());

  it("renders the code content", () => {
    const { container } = render(
      <CopyableSnippet code="npm run build" isCopyable />,
    );
    // The syntax highlighter splits tokens across nodes, so match on text content.
    expect(container.textContent).toContain("npm run build");
  });

  it("hides the copy button when not copyable", () => {
    render(<CopyableSnippet code="secret" isCopyable={false} />);
    expect(screen.queryByLabelText("Copy code")).toBeNull();
  });

  it("copies via the clipboard API", () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.assign(navigator, { clipboard: { writeText } });
    render(<CopyableSnippet code="value" isCopyable />);
    fireEvent.click(screen.getByLabelText("Copy code"));
    expect(writeText).toHaveBeenCalledWith("value");
  });

  it("falls back to execCommand when clipboard is unavailable", () => {
    Object.assign(navigator, { clipboard: undefined });
    const exec = vi.fn();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (document as any).execCommand = exec;
    render(<CopyableSnippet code="fallback" language="json" isCopyable />);
    fireEvent.click(screen.getByLabelText("Copy code"));
    expect(exec).toHaveBeenCalledWith("copy");
  });
});
