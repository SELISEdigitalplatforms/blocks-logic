import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";

vi.mock("@monaco-editor/react", () => ({
  default: ({ value }: { value?: string }) => <textarea data-testid="viewer" readOnly value={value ?? ""} />,
}));

import { ExecutionResponseSection } from "./execution-response-section";

describe("ExecutionResponseSection", () => {
  it("shows the target's body", () => {
    renderWithProviders(<ExecutionResponseSection body='{"ok":true}' statusLabel="HTTP 200" />);
    expect((screen.getByTestId("viewer") as HTMLTextAreaElement).value).toBe('{"ok":true}');
    expect(screen.queryByText(/Recorded by the scheduler/)).toBeNull();
    expect(screen.queryByText(/truncated/)).toBeNull();
  });

  it("marks a body the scheduler recorded itself", () => {
    renderWithProviders(<ExecutionResponseSection body="Network error: no such host" statusLabel="HTTP 502" />);
    expect(screen.getByText("Recorded by the scheduler — no response from the target.")).toBeTruthy();
    expect(screen.getByText("HTTP 502")).toBeTruthy();
  });

  it("notes a truncated body", () => {
    renderWithProviders(<ExecutionResponseSection body={"<html>…\n…[truncated]"} statusLabel="HTTP 500" />);
    expect(screen.getByText("Body truncated to 16 KB.")).toBeTruthy();
  });

  it("says when there is no body", () => {
    renderWithProviders(<ExecutionResponseSection body={null} statusLabel="HTTP 204" />);
    expect(screen.getByText("No body.")).toBeTruthy();
    expect(screen.queryByTestId("viewer")).toBeNull();
  });
});
