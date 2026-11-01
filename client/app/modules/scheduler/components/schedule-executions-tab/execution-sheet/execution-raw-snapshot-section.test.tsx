import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";

vi.mock("@monaco-editor/react", () => ({
  default: ({ value, language }: { value?: string; language?: string }) => (
    <textarea data-testid="viewer" data-language={language} readOnly value={value ?? ""} />
  ),
}));

import { ExecutionRawSnapshotSection } from "./execution-raw-snapshot-section";

describe("ExecutionRawSnapshotSection", () => {
  it("is collapsed by default and expands to the JSON viewer", async () => {
    renderWithProviders(<ExecutionRawSnapshotSection value={'{\n  "a": 1\n}'} />);
    expect(screen.queryByTestId("viewer")).toBeNull();

    await userEvent.click(screen.getByRole("button", { name: /raw snapshot \(json\)/i }));

    expect(screen.getByTestId("viewer").getAttribute("data-language")).toBe("json");
    expect(screen.getByText("Secrets are redacted.")).toBeTruthy();
  });

  it("is expanded as plain text for an invalid snapshot", () => {
    renderWithProviders(<ExecutionRawSnapshotSection value="{oops" invalid />);
    const viewer = screen.getByTestId("viewer") as HTMLTextAreaElement;
    expect(viewer.value).toBe("{oops");
    expect(viewer.getAttribute("data-language")).toBe("plaintext");
  });
});
