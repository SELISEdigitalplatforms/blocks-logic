import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { RunEvents } from "./run-events";

const getRunEvents = vi.fn();
vi.mock("../../services/function.service", () => ({
  functionService: { getRunEvents: (...args: unknown[]) => getRunEvents(...args) },
}));

describe("RunEvents", () => {
  beforeEach(() => vi.clearAllMocks());

  it("lists the events with a status chip, error and attempts", async () => {
    getRunEvents.mockResolvedValue({
      rejected: null,
      items: [
        { id: "e1", type: "FunctionEvent", name: "order.paid", status: "Sent", error: null, attempts: 1, sentAt: null },
        { id: "e2", type: "SendMail", name: null, status: "Failed", error: "SmtpDown", attempts: 3, sentAt: null },
        { id: "e3", type: "SendMail", name: null, status: "Rejected", error: null, attempts: 0, sentAt: null },
      ],
    });

    renderWithProviders(<RunEvents runId="run_1" />);

    expect(await screen.findByText("order.paid")).toBeTruthy();
    expect(screen.getByText("Sent")).toBeTruthy();
    expect(screen.getByText("Failed")).toBeTruthy();
    expect(screen.getByText("Rejected")).toBeTruthy();
    expect(screen.getByText("SmtpDown")).toBeTruthy();
    expect(screen.getByText("3 tries")).toBeTruthy();
    expect(getRunEvents).toHaveBeenCalledWith("run_1");
  });

  it("shows a banner when events were dropped", async () => {
    getRunEvents.mockResolvedValue({ items: [], rejected: "too many events" });

    renderWithProviders(<RunEvents runId="run_1" />);

    expect((await screen.findByRole("alert")).textContent).toContain("Events dropped: too many events");
  });

  it("renders nothing when there are no events and nothing was dropped", async () => {
    getRunEvents.mockResolvedValue({ items: [], rejected: null });

    const { container } = renderWithProviders(<RunEvents runId="run_1" />);

    await vi.waitFor(() => expect(getRunEvents).toHaveBeenCalled());
    expect(container.textContent).toBe("");
  });
});
