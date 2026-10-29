import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";

vi.mock("@monaco-editor/react", () => ({
  default: ({ value, language, theme }: { value?: string; language?: string; theme?: string }) => (
    <textarea data-testid="viewer" data-language={language} data-theme={theme} readOnly value={value ?? ""} />
  ),
}));

const theme = vi.hoisted(() => ({ resolvedTheme: "light" }));
vi.mock("@seliseblocks/genesis-os/hooks", () => ({ useTheme: () => theme }));

import { detectLanguage, ReadOnlyCodeViewer } from "./read-only-code-viewer";

describe("detectLanguage", () => {
  it("is json only for text that parses", () => {
    expect(detectLanguage('{"ok":true}')).toBe("json");
    expect(detectLanguage("<html><script>alert(1)</script></html>")).toBe("plaintext");
  });
});

describe("ReadOnlyCodeViewer", () => {
  it("shows JSON as json with a copy button", () => {
    renderWithProviders(<ReadOnlyCodeViewer value='{"ok":true}' label="Response body" />);
    const viewer = screen.getByTestId("viewer");
    expect(viewer.getAttribute("data-language")).toBe("json");
    expect(viewer.getAttribute("data-theme")).toBe("vs");
    expect(screen.getByRole("button", { name: "Copy response body" })).toBeTruthy();
  });

  it("shows HTML as plain text, never rendered", () => {
    const html = "<b>bold</b><script>alert(1)</script>";
    const { container } = renderWithProviders(<ReadOnlyCodeViewer value={html} label="Response body" />);
    expect((screen.getByTestId("viewer") as HTMLTextAreaElement).value).toBe(html);
    expect(screen.getByTestId("viewer").getAttribute("data-language")).toBe("plaintext");
    expect(container.querySelector("b")).toBeNull();
    expect(container.querySelector("script")).toBeNull();
  });

  it("follows the app's dark theme", () => {
    theme.resolvedTheme = "dark";
    renderWithProviders(<ReadOnlyCodeViewer value="text" label="Payload" />);
    expect(screen.getByTestId("viewer").getAttribute("data-theme")).toBe("vs-dark");
    theme.resolvedTheme = "light";
  });

  it("honours a forced language", () => {
    renderWithProviders(<ReadOnlyCodeViewer value='{"a":1}' label="Snapshot" language="plaintext" />);
    expect(screen.getByTestId("viewer").getAttribute("data-language")).toBe("plaintext");
  });
});
