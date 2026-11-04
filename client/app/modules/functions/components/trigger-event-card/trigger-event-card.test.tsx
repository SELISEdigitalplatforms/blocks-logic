import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { TriggerEventCard } from "./trigger-event-card";

const getEventSettings = vi.fn();
const saveEventSettings = vi.fn();
const getForFunction = vi.fn();
vi.mock("../../services/function.service", () => ({
  functionService: {
    getEventSettings: (...args: unknown[]) => getEventSettings(...args),
    saveEventSettings: (...args: unknown[]) => saveEventSettings(...args),
  },
}));
vi.mock("@/modules/actions/services/actions.service", () => ({
  actionsService: {
    getForFunction: (...args: unknown[]) => getForFunction(...args),
  },
}));

const CREATED = "iam.user.created";
const UPDATED = "iam.user.updated";
const ACTIONS = [
  { name: CREATED, label: "User created", service: "iam", payload: { userId: "string" } },
  { name: UPDATED, label: "User updated", service: "iam", payload: { userId: "string", by: "string" } },
  { name: "localization.files.generated", label: "Files generated", service: "localization", payload: {} },
];
const SEARCH = "Search actions";

describe("TriggerEventCard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getEventSettings.mockResolvedValue({ functionId: "fn_1", triggerEvents: [CREATED] });
    saveEventSettings.mockResolvedValue({ isSuccess: true });
    getForFunction.mockResolvedValue(ACTIONS);
  });

  it("shows the picked actions by label and name, and the queue note", async () => {
    renderWithProviders(<TriggerEventCard functionId="fn_1" />);

    expect(await screen.findByText("User created (iam.user.created)")).toBeTruthy();
    expect(screen.getByText(/1 action per 5 HTTP/)).toBeTruthy();
  });

  it("lists only the actions on for functions that are not picked yet, with their data fields", async () => {
    renderWithProviders(<TriggerEventCard functionId="fn_1" />);

    const list = await screen.findByRole("list", { name: "Available actions" });
    expect(list.textContent).toContain("User updated (iam.user.updated)");
    expect(list.textContent).toContain("userId (string), by (string)");
    expect(screen.queryByRole("button", { name: `Add ${CREATED}` })).toBeNull();
  });

  it("the search narrows the list by label, name or service", async () => {
    renderWithProviders(<TriggerEventCard functionId="fn_1" />);
    await screen.findByRole("list", { name: "Available actions" });

    fireEvent.change(screen.getByLabelText(SEARCH), { target: { value: "locali" } });

    expect(screen.getByRole("button", { name: "Add localization.files.generated" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: `Add ${UPDATED}` })).toBeNull();
  });

  it("adds a picked action and saves the list", async () => {
    renderWithProviders(<TriggerEventCard functionId="fn_1" />);

    fireEvent.click(await screen.findByRole("button", { name: `Add ${UPDATED}` }));

    await waitFor(() =>
      expect(saveEventSettings).toHaveBeenCalledWith({ functionId: "fn_1", triggerEvents: [CREATED, UPDATED] }),
    );
  });

  it("removes an action", async () => {
    renderWithProviders(<TriggerEventCard functionId="fn_1" />);
    await screen.findByText("User created (iam.user.created)");

    fireEvent.click(screen.getByRole("button", { name: `Remove ${CREATED}` }));

    await waitFor(() => expect(saveEventSettings).toHaveBeenCalledWith({ functionId: "fn_1", triggerEvents: [] }));
  });

  it("a saved action that is no longer on for functions stays and says so", async () => {
    getEventSettings.mockResolvedValue({ functionId: "fn_1", triggerEvents: ["iam.user.deactivated"] });

    renderWithProviders(<TriggerEventCard functionId="fn_1" />);

    expect(await screen.findByText("iam.user.deactivated (not on for functions)")).toBeTruthy();
  });

  it("with no action on for functions it says where an admin turns them on", async () => {
    getForFunction.mockResolvedValue([]);

    renderWithProviders(<TriggerEventCard functionId="fn_1" />);

    expect(await screen.findByText(/No actions are on for functions/)).toBeTruthy();
    expect(screen.queryByLabelText(SEARCH)).toBeNull();
  });
});
