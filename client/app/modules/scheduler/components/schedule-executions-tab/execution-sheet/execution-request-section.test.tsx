import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { queueSnapshot, webhookSnapshot } from "../../../__mocks__/schedule-execution.mock";
import { parseSnapshot, ScheduleSnapshot } from "../parse-snapshot";

vi.mock("@monaco-editor/react", () => ({
  default: ({ value }: { value?: string }) => <textarea data-testid="viewer" readOnly value={value ?? ""} />,
}));

import { ExecutionRequestSection } from "./execution-request-section";

const snapshotOf = (raw: string): ScheduleSnapshot => {
  const parsed = parseSnapshot(raw);
  if (parsed.kind !== "ok") throw new Error("fixture must parse");
  return parsed.snapshot;
};

describe("ExecutionRequestSection", () => {
  it("shows a webhook request with header names only and the signing state", () => {
    renderWithProviders(<ExecutionRequestSection snapshot={snapshotOf(webhookSnapshot)} />);
    expect(screen.getByText("Webhook")).toBeTruthy();
    expect(screen.getByText("POST")).toBeTruthy();
    expect(screen.getByText("https://hooks.example.com/x?k=1")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Copy webhook url" })).toBeTruthy();
    expect(screen.getByText("Authorization")).toBeTruthy();
    expect(screen.getByText("[redacted]")).toBeTruthy();
    expect(screen.getByText("Signed (x-signature-sha256)")).toBeTruthy();
    expect((screen.getByTestId("viewer") as HTMLTextAreaElement).value).toBe('{"a":1}');
  });

  it("shows an unsigned webhook with no headers or URL", () => {
    const snapshot = snapshotOf(JSON.stringify({ triggerType: 1, webhook: { method: "GET" } }));
    renderWithProviders(<ExecutionRequestSection snapshot={snapshot} />);
    expect(screen.getByText("No custom headers")).toBeTruthy();
    expect(screen.getByText("Not signed")).toBeTruthy();
    expect(screen.getByText("No payload")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Copy webhook url" })).toBeNull();
  });

  it("shows a queue request", () => {
    renderWithProviders(<ExecutionRequestSection snapshot={snapshotOf(queueSnapshot)} />);
    expect(screen.getByText("Queue")).toBeTruthy();
    expect(screen.getByText("orders-queue")).toBeTruthy();
    expect(screen.queryByText("Signed (x-signature-sha256)")).toBeNull();
  });

  it("renders a dash for an unknown trigger type", () => {
    renderWithProviders(<ExecutionRequestSection snapshot={snapshotOf("{}")} />);
    expect(screen.getByText("—")).toBeTruthy();
  });
});
