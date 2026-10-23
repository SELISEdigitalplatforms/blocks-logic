import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { TriggerEventCard } from "./trigger-event-card";

const getEventSettings = vi.fn();
const saveEventSettings = vi.fn();
vi.mock("../../services/function.service", () => ({
  functionService: {
    getEventSettings: (...args: unknown[]) => getEventSettings(...args),
    saveEventSettings: (...args: unknown[]) => saveEventSettings(...args),
  },
}));

const CREATED = "order.created";
const SETTINGS = {
  functionId: "fn_1",
  events: [{ type: "SendMail", payload: { to: "a" } }],
  triggerEvents: [CREATED],
};
const EVENT_INPUT = "Event name";

describe("TriggerEventCard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getEventSettings.mockResolvedValue(SETTINGS);
    saveEventSettings.mockResolvedValue({ isSuccess: true });
  });

  it("shows the names and the queue note", async () => {
    renderWithProviders(<TriggerEventCard functionId="fn_1" />);

    expect(await screen.findByText(CREATED)).toBeTruthy();
    expect(screen.getByText(/1 event per 5 HTTP/)).toBeTruthy();
    expect(screen.getByText(/5 minutes/)).toBeTruthy();
  });

  it("adds a name and saves it with the latest events", async () => {
    renderWithProviders(<TriggerEventCard functionId="fn_1" />);
    await screen.findByText(CREATED);

    fireEvent.change(screen.getByLabelText(EVENT_INPUT), { target: { value: "order.paid" } });
    fireEvent.click(screen.getByRole("button", { name: /Add/ }));

    await waitFor(() =>
      expect(saveEventSettings).toHaveBeenCalledWith({
        functionId: "fn_1",
        events: SETTINGS.events,
        triggerEvents: [CREATED, "order.paid"],
      }),
    );
  });

  it("refuses an invalid name and a duplicate without saving", async () => {
    renderWithProviders(<TriggerEventCard functionId="fn_1" />);
    await screen.findByText(CREATED);

    fireEvent.change(screen.getByLabelText(EVENT_INPUT), { target: { value: "Bad Name" } });
    fireEvent.click(screen.getByRole("button", { name: /Add/ }));
    expect((await screen.findByRole("alert")).textContent).toContain("Not a valid event name");

    fireEvent.change(screen.getByLabelText(EVENT_INPUT), { target: { value: CREATED } });
    fireEvent.click(screen.getByRole("button", { name: /Add/ }));
    await waitFor(() => expect(screen.getByRole("alert").textContent).toContain("already"));
    expect(saveEventSettings).not.toHaveBeenCalled();
  });

  it("removes a name", async () => {
    renderWithProviders(<TriggerEventCard functionId="fn_1" />);
    await screen.findByText(CREATED);

    fireEvent.click(screen.getByRole("button", { name: "Remove order.created" }));

    await waitFor(() =>
      expect(saveEventSettings).toHaveBeenCalledWith({
        functionId: "fn_1",
        events: SETTINGS.events,
        triggerEvents: [],
      }),
    );
  });
});
