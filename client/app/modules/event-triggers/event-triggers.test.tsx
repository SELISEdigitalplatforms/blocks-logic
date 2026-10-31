import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { navigationMenus } from "@/constants/navigation-menus";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { EventTriggers, NOT_SEEDED_MESSAGE } from "./pages/event-triggers/event-triggers";
import { ManageBlocksEventsLink } from "./components/manage-blocks-events-link/manage-blocks-events-link";
import { eventTriggersService } from "./services/event-triggers.service";
import { EVENT_TRIGGERS_ENDPOINTS } from "./constants/endpoint.constant";
import { filterEvents, DEFAULT_EVENT_TRIGGER_FILTERS } from "./utils/filter-events";
import { IBlocksEventCatalogItem } from "./types/event-triggers.type";

vi.mock("@seliseblocks/genesis-os", async () => {
  const actual = await vi.importActual("@seliseblocks/genesis-os");
  return {
    ...actual,
    useScopedPath: () => (path: string) => `/app/item-123/${path.replace(/^\//, "")}`,
  };
});

vi.mock("@/hooks/use-toast", async () => {
  const actual = await vi.importActual("@/hooks/use-toast");
  return { ...actual, showSuccessToast: vi.fn(), showErrorToast: vi.fn() };
});

const item = (eventName: string, label: string, serviceKey: string, workflow = false, fn = false): IBlocksEventCatalogItem => ({
  eventName,
  label,
  description: `${label} description`,
  serviceKey,
  sourceServiceKeys: [serviceKey],
  consumerNames: [`q_${eventName}`],
  isValidForWorkflow: workflow,
  isValidForFunction: fn,
});

const CATALOG = [
  item("UserMutationEvent", "User changed", "iam"),
  item("LogoutAllEvent", "Logout all sessions", "iam", false, true),
  item("SmsStatusEvent", "SMS status", "utilities", true),
];

const forbidden = Object.assign(new Error("Forbidden"), { status: 403 });

const setup = ({ canManage = true, isSeeded = true, items = CATALOG } = {}) => {
  vi.spyOn(eventTriggersService, "getAll").mockResolvedValue({ isSeeded, items: isSeeded ? items : [] });
  vi.spyOn(eventTriggersService, "canManage").mockImplementation(() =>
    canManage ? Promise.resolve({ canManage: true }) : Promise.reject(forbidden),
  );
  const update = vi.spyOn(eventTriggersService, "update");
  const usage = vi.spyOn(eventTriggersService, "getUsage").mockResolvedValue({ publishedWorkflowCount: 0 });
  renderWithProviders(
    <MemoryRouter>
      <EventTriggers />
    </MemoryRouter>,
  );
  return { update, usage };
};

const workflowSwitch = (label: string) => screen.getByRole("switch", { name: `Enable ${label} for workflows` });
const functionSwitch = (label: string) => screen.getByRole("switch", { name: `Enable ${label} for functions` });

describe("Event Triggers page", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.mocked(showSuccessToast).mockClear();
    vi.mocked(showErrorToast).mockClear();
  });

  it("H6: lists every event with label, mono name, service and both switches", async () => {
    setup();

    expect(await screen.findByText("User changed")).toBeTruthy();
    expect(screen.getByText("UserMutationEvent").tagName).toBe("CODE");
    expect(screen.getAllByText("iam").length).toBeGreaterThan(0);
    expect(workflowSwitch("SMS status").getAttribute("aria-checked")).toBe("true");
    expect(functionSwitch("Logout all sessions").getAttribute("aria-checked")).toBe("true");
    expect(workflowSwitch("User changed").getAttribute("aria-checked")).toBe("false");
  });

  it("a manager sees no read-only notice", async () => {
    setup();
    await screen.findByText("User changed");
    expect(screen.queryByText(/to change these settings/)).toBeNull();
  });

  it("C12: a tenant without a catalog sees the not-seeded alert and no switches", async () => {
    setup({ isSeeded: false });

    expect(await screen.findByText(NOT_SEEDED_MESSAGE)).toBeTruthy();
    expect(screen.queryAllByRole("switch")).toHaveLength(0);
  });

  it("H9: search and the Enabled-for chips filter rows without a server call", async () => {
    setup();
    await screen.findByText("User changed");
    const calls = vi.mocked(eventTriggersService.getAll).mock.calls.length;

    await userEvent.type(screen.getByLabelText("Search events"), "UserMutation");
    expect(screen.getByText("User changed")).toBeTruthy();
    expect(screen.queryByText("SMS status")).toBeNull();

    await userEvent.clear(screen.getByLabelText("Search events"));
    await userEvent.click(screen.getByRole("button", { name: "Enabled for workflows" }));
    expect(screen.getByText("SMS status")).toBeTruthy();
    expect(screen.queryByText("User changed")).toBeNull();

    await userEvent.type(screen.getByLabelText("Search events"), "zzz");
    expect(screen.getByText("No events match your filters.")).toBeTruthy();
    expect(vi.mocked(eventTriggersService.getAll).mock.calls.length).toBe(calls);
  });

  it("H7: a toggle saves only that flag and confirms with a toast", async () => {
    const success = vi.mocked(showSuccessToast);
    const { update } = setup();
    update.mockResolvedValue({ isSuccess: true, itemId: "UserMutationEvent", errors: null, affectedPublishedWorkflowCount: 0 });
    await screen.findByText("User changed");

    await userEvent.click(workflowSwitch("User changed"));

    await waitFor(() =>
      expect(update).toHaveBeenCalledWith({ eventName: "UserMutationEvent", isValidForWorkflow: true }),
    );
    await waitFor(() => expect(workflowSwitch("User changed").getAttribute("aria-checked")).toBe("true"));
    expect(success).toHaveBeenCalledWith({ description: "User changed enabled for workflows" });
  });

  it("C14: a failed save reverts only that switch and shows the error", async () => {
    const error = vi.mocked(showErrorToast);
    const { update } = setup();
    update.mockResolvedValue({ isSuccess: false, itemId: "UserMutationEvent", errors: { Message: "boom" }, affectedPublishedWorkflowCount: 0 });
    await screen.findByText("User changed");

    await userEvent.click(functionSwitch("User changed"));

    await waitFor(() => expect(error).toHaveBeenCalled());
    await waitFor(() => expect(functionSwitch("User changed").getAttribute("aria-checked")).toBe("false"));
    expect(workflowSwitch("SMS status").getAttribute("aria-checked")).toBe("true");
  });

  it("H10: turning Workflow off for a used event asks first and saves only after Disable", async () => {
    const { update, usage } = setup();
    usage.mockResolvedValue({ publishedWorkflowCount: 2 });
    update.mockResolvedValue({ isSuccess: true, itemId: "SmsStatusEvent", errors: null, affectedPublishedWorkflowCount: 2 });
    await screen.findByText("SMS status");

    await userEvent.click(workflowSwitch("SMS status"));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/2 published workflow\(s\) use this event/)).toBeTruthy();
    expect(update).not.toHaveBeenCalled();

    await userEvent.click(within(dialog).getByRole("button", { name: "Disable" }));
    await waitFor(() =>
      expect(update).toHaveBeenCalledWith({ eventName: "SmsStatusEvent", isValidForWorkflow: false }),
    );
  });

  it("H10: Cancel keeps the event enabled and saves nothing", async () => {
    const { update, usage } = setup();
    usage.mockResolvedValue({ publishedWorkflowCount: 1 });
    await screen.findByText("SMS status");

    await userEvent.click(workflowSwitch("SMS status"));
    await userEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Cancel" }));

    expect(update).not.toHaveBeenCalled();
    expect(workflowSwitch("SMS status").getAttribute("aria-checked")).toBe("true");
  });

  it("C8: without manage permission every switch is disabled", async () => {
    const { update } = setup({ canManage: false });
    await screen.findByText("User changed");

    await waitFor(() => expect(screen.getAllByRole("switch").every((s) => s.hasAttribute("disabled"))).toBe(true));
    expect(screen.getByText("You need workflow manage permission to change these settings.")).toBeTruthy();
    await userEvent.click(workflowSwitch("User changed"));
    expect(update).not.toHaveBeenCalled();
  });
});

describe("Manage events link and navigation", () => {
  it("H15: opens the project's Event Triggers page in a new tab", () => {
    renderWithProviders(<ManageBlocksEventsLink />);

    const link = screen.getByRole("link", { name: "Manage events (opens in a new tab)" });
    expect(link.getAttribute("href")).toBe("/app/item-123/event-triggers");
    expect(link.getAttribute("target")).toBe("_blank");
    expect(link.getAttribute("rel")).toBe("noopener noreferrer");
  });

  it("H16: the console menu has no Event Triggers item", () => {
    const flatten = (menus: unknown[]): string[] =>
      menus.flatMap((menu) => {
        const m = menu as { path?: string; name?: string; children?: unknown[] };
        return [m.path ?? "", m.name ?? "", ...flatten(m.children ?? [])];
      });
    const entries = flatten(navigationMenus as unknown[]).join(" ").toLowerCase();
    expect(entries).not.toContain("event-triggers");
    expect(entries).not.toContain("event triggers");
  });

  it("calls the BlocksEventTrigger endpoints", () => {
    expect(EVENT_TRIGGERS_ENDPOINTS.GET_ALL).toMatch(/\/BlocksEventTrigger\/GetAll$/);
    expect(EVENT_TRIGGERS_ENDPOINTS.UPDATE).toMatch(/\/BlocksEventTrigger\/Update$/);
    expect(EVENT_TRIGGERS_ENDPOINTS.CAN_MANAGE).toMatch(/\/BlocksEventTrigger\/CanManage$/);
  });

  it("filters by service key", () => {
    const rows = filterEvents(CATALOG, { ...DEFAULT_EVENT_TRIGGER_FILTERS, serviceKey: "utilities" });
    expect(rows.map((r) => r.eventName)).toEqual(["SmsStatusEvent"]);
  });
});
