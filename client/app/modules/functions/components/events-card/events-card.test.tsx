import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { EventsCard } from "./events-card";

const getEventCatalog = vi.fn();
const getEventSettings = vi.fn();
const saveEventSettings = vi.fn();
vi.mock("../../services/function.service", () => ({
  functionService: {
    getEventCatalog: (...args: unknown[]) => getEventCatalog(...args),
    getEventSettings: (...args: unknown[]) => getEventSettings(...args),
    saveEventSettings: (...args: unknown[]) => saveEventSettings(...args),
  },
}));
vi.mock("../code-editor", () => ({ CodeEditor: () => <div /> }));

const CATALOG = {
  items: [
    { type: "SendMail", title: "Send mail", description: "", publicAllowed: false, payloadTemplate: { to: "" } },
    { type: "FunctionEvent", title: "Function event", description: "", publicAllowed: true, payloadTemplate: { name: "" } },
  ],
};
const SETTINGS = {
  functionId: "fn_1",
  events: [
    { type: "SendMail", payload: { to: "a@b.c" } },
    { type: "FunctionEvent", payload: { name: "order.paid" } },
  ],
  triggerEvents: ["order.created"],
};

describe("EventsCard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getEventCatalog.mockResolvedValue(CATALOG);
    getEventSettings.mockResolvedValue(SETTINGS);
    saveEventSettings.mockResolvedValue({ isSuccess: true });
  });

  it("lists the added events with the FunctionEvent name and the deploy note", async () => {
    renderWithProviders(<EventsCard functionId="fn_1" isPublic={false} />);

    expect(await screen.findByText("Send mail")).toBeTruthy();
    expect(screen.getByText("order.paid")).toBeTruthy();
    expect(screen.getByText(/after the next Deploy/)).toBeTruthy();
    expect(screen.getByText(/only after the function succeeds/)).toBeTruthy();
  });

  it("removing an event saves the rest and keeps the trigger events", async () => {
    renderWithProviders(<EventsCard functionId="fn_1" isPublic={false} />);
    await screen.findByText("Send mail");

    fireEvent.click(screen.getAllByRole("button", { name: "Remove" })[0]);

    await waitFor(() =>
      expect(saveEventSettings).toHaveBeenCalledWith({
        functionId: "fn_1",
        events: [SETTINGS.events[1]],
        triggerEvents: ["order.created"],
      }),
    );
  });

  it("a 400 puts the field message on its event row", async () => {
    saveEventSettings.mockRejectedValue({
      errors: { invalid_request: 'events[1].payload: unknown field "x"' },
    });
    renderWithProviders(<EventsCard functionId="fn_1" isPublic={false} />);
    await screen.findByText("Send mail");

    fireEvent.click(screen.getAllByRole("button", { name: "Remove" })[0]);

    expect((await screen.findByRole("alert")).textContent).toBe('unknown field "x"');
  });

  it("copies the ctx.events.add snippet", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
    renderWithProviders(<EventsCard functionId="fn_1" isPublic={false} />);
    await screen.findByText("Send mail");

    fireEvent.click(screen.getByRole("button", { name: "Copy snippet for FunctionEvent" }));

    await waitFor(() =>
      expect(writeText).toHaveBeenCalledWith(expect.stringMatching(/^ctx\.events\.add\('FunctionEvent', /)),
    );
  });

  it("opens the add dialog", async () => {
    renderWithProviders(<EventsCard functionId="fn_1" isPublic />);
    await screen.findByText("Send mail");

    fireEvent.click(screen.getByRole("button", { name: /Add event/ }));

    expect(await screen.findByRole("dialog")).toBeTruthy();
  });
});
