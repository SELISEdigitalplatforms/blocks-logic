import { useState } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { ITriggerConfig } from "../../types/function.types";
import { TriggerTab } from "./trigger-tab";

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
vi.mock("@/modules/proxy/hooks", () => ({
  useIamRoles: () => ({ data: [], isLoading: false }),
  useIamPermissions: () => ({ data: [], isLoading: false }),
}));

const CREATED = "iam.user.created";
const UPDATED = "iam.user.updated";
const ACTIONS = [
  { name: CREATED, label: "A user is created", service: "iam", payload: {} },
  { name: UPDATED, label: "A user is updated", service: "iam", payload: {} },
];

const TRIGGER: ITriggerConfig = {
  httpEnabled: true,
  httpMethod: "Post",
  httpMethods: [],
  reuseSandbox: false,
  responseMode: "async",
  authMode: "Token",
  roles: [],
  permissions: [],
  roleMatch: "Any",
  permissionMatch: "Any",
  combine: "Or",
  workflowEnabled: true,
};

const ARIA_CHECKED = "aria-checked";
const WEB_SWITCH = "Allow web requests";
const ACTIONS_SWITCH = "Run on actions";
const WORKFLOW_SWITCH = "Allow in workflows";
const WEB_TOGGLE = "Show or hide Web request";
const ACTIONS_TOGGLE = "Show or hide Actions";
const WORKFLOW_TOGGLE = "Show or hide Workflow step";
const CANNOT_START = "This function cannot start: turn on at least one of the ways below.";

const Harness = ({
  initial,
  onTriggerChange,
}: Readonly<{ initial: ITriggerConfig; onTriggerChange: (value: ITriggerConfig) => void }>) => {
  const [trigger, setTrigger] = useState(initial);
  return (
    <TriggerTab
      functionId="fn_1"
      trigger={trigger}
      onTriggerChange={(next) => {
        setTrigger(next);
        onTriggerChange(next);
      }}
      requestsPerMinute={null}
      onRequestsPerMinuteChange={vi.fn()}
    />
  );
};

const renderTab = (initial: ITriggerConfig = TRIGGER) => {
  const onTriggerChange = vi.fn();
  renderWithProviders(<Harness initial={initial} onTriggerChange={onTriggerChange} />);
  return onTriggerChange;
};

const switchOf = (name: string) => screen.getByRole("switch", { name });

describe("TriggerTab", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getEventSettings.mockResolvedValue({
      functionId: "fn_1",
      triggerEvents: [],
      actionsEnabled: true,
    });
    saveEventSettings.mockResolvedValue({ isSuccess: true });
    getForFunction.mockResolvedValue(ACTIONS);
  });

  it("starts with every way switched on and every section closed", async () => {
    renderTab();

    await waitFor(() => expect(switchOf(ACTIONS_SWITCH).hasAttribute("disabled")).toBe(false));
    for (const name of [WEB_SWITCH, ACTIONS_SWITCH, WORKFLOW_SWITCH]) {
      expect(switchOf(name).getAttribute(ARIA_CHECKED)).toBe("true");
    }
    for (const name of [WEB_TOGGLE, ACTIONS_TOGGLE, WORKFLOW_TOGGLE]) {
      expect(screen.getByRole("button", { name }).getAttribute("aria-expanded")).toBe("false");
    }
    expect(screen.queryByText("Address")).toBeNull();
    expect(screen.queryByText("Step before")).toBeNull();
    expect(screen.queryByRole("combobox", { name: "Action" })).toBeNull();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("opens a section's body with its chevron and closes it again", async () => {
    renderTab();

    await userEvent.click(screen.getByRole("button", { name: WEB_TOGGLE }));
    expect(screen.getByText("Address")).toBeTruthy();
    expect(screen.getByText("Max calls per minute")).toBeTruthy();
    expect(screen.getByText("Your function gets")).toBeTruthy();

    await userEvent.click(screen.getByRole("button", { name: WEB_TOGGLE }));
    expect(screen.queryByText("Address")).toBeNull();
  });

  it("the web request switch is editable and saved on the trigger", async () => {
    const onTriggerChange = renderTab();

    await userEvent.click(switchOf(WEB_SWITCH));

    expect(onTriggerChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ httpEnabled: false }),
    );
    expect(switchOf(WEB_SWITCH).getAttribute(ARIA_CHECKED)).toBe("false");
  });

  it("the workflow switch sets workflowEnabled, and its body shows the three steps", async () => {
    const onTriggerChange = renderTab();

    await userEvent.click(switchOf(WORKFLOW_SWITCH));
    expect(onTriggerChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ workflowEnabled: false }),
    );

    await userEvent.click(screen.getByRole("button", { name: WORKFLOW_TOGGLE }));
    for (const text of ["Step before", "This function", "Next step"]) {
      expect(screen.getByText(text)).toBeTruthy();
    }
  });

  it("opens the repeat-call note on request", async () => {
    renderTab();
    expect(screen.queryByTestId("reuse-rules")).toBeNull();

    await userEvent.click(screen.getByRole("button", { name: "Why repeat calls are faster" }));

    expect(screen.getByTestId("reuse-rules").textContent).toMatch(/Tests always start fresh/);
  });

  describe("actions", () => {
    const openActions = async () => {
      await waitFor(() => expect(switchOf(ACTIONS_SWITCH).hasAttribute("disabled")).toBe(false));
      await userEvent.click(screen.getByRole("button", { name: ACTIONS_TOGGLE }));
    };

    it("picking an action saves just that one name and keeps the switch", async () => {
      renderTab();
      await openActions();

      await userEvent.click(await screen.findByRole("combobox", { name: "Action" }));
      await userEvent.click(await screen.findByRole("option", { name: /A user is updated/ }));

      await waitFor(() =>
        expect(saveEventSettings).toHaveBeenCalledWith({
          functionId: "fn_1",
          triggerEvents: [UPDATED],
          actionsEnabled: true,
        }),
      );
    });

    it("the switch saves actionsEnabled and keeps the saved action", async () => {
      getEventSettings.mockResolvedValue({
        functionId: "fn_1",
        triggerEvents: [CREATED],
        actionsEnabled: true,
      });
      renderTab();
      await waitFor(() => expect(switchOf(ACTIONS_SWITCH).hasAttribute("disabled")).toBe(false));

      await userEvent.click(switchOf(ACTIONS_SWITCH));

      await waitFor(() =>
        expect(saveEventSettings).toHaveBeenCalledWith({
          functionId: "fn_1",
          triggerEvents: [CREATED],
          actionsEnabled: false,
        }),
      );
    });

    it("settings with no actionsEnabled show the switch on", async () => {
      getEventSettings.mockResolvedValue({ functionId: "fn_1", triggerEvents: [] });
      renderTab();

      await waitFor(() => expect(switchOf(ACTIONS_SWITCH).hasAttribute("disabled")).toBe(false));
      expect(switchOf(ACTIONS_SWITCH).getAttribute(ARIA_CHECKED)).toBe("true");
    });

    it("clearing the action saves an empty list", async () => {
      getEventSettings.mockResolvedValue({
        functionId: "fn_1",
        triggerEvents: [CREATED],
        actionsEnabled: true,
      });
      renderTab();
      await openActions();

      await userEvent.click(await screen.findByRole("button", { name: "Clear" }));

      await waitFor(() =>
        expect(saveEventSettings).toHaveBeenCalledWith({
          functionId: "fn_1",
          triggerEvents: [],
          actionsEnabled: true,
        }),
      );
    });

    it("a saved action that is no longer on for functions stays and says so", async () => {
      getEventSettings.mockResolvedValue({
        functionId: "fn_1",
        triggerEvents: ["iam.user.deactivated"],
        actionsEnabled: true,
      });
      renderTab();
      await openActions();

      const select = await screen.findByRole("combobox", { name: "Action" });
      await waitFor(() =>
        expect(select.textContent).toContain("iam.user.deactivated (not on for functions)"),
      );
    });

    it("with no action on for functions it says where an admin turns them on", async () => {
      getForFunction.mockResolvedValue([]);
      renderTab();
      await openActions();

      const note = await screen.findByText(/Nothing to choose yet/);
      expect(within(note).getByRole("link", { name: "Blocks OS → Config → Actions" })).toBeTruthy();
      expect(screen.getByRole("combobox", { name: "Action" }).hasAttribute("disabled")).toBe(true);
    });

    it("the info button opens the action details, even from a closed section", async () => {
      renderTab();
      await waitFor(() => expect(switchOf(ACTIONS_SWITCH).hasAttribute("disabled")).toBe(false));

      await userEvent.click(screen.getByRole("button", { name: "About actions" }));

      expect(await screen.findByText(/gets the action details/)).toBeTruthy();
    });
  });

  describe("all ways off", () => {
    it("warns that the function cannot start, and hides the warning once one way is on", async () => {
      getEventSettings.mockResolvedValue({
        functionId: "fn_1",
        triggerEvents: [],
        actionsEnabled: false,
      });
      renderTab({ ...TRIGGER, httpEnabled: false, workflowEnabled: false });

      expect((await screen.findByRole("alert")).textContent).toContain(CANNOT_START);

      await userEvent.click(switchOf(WEB_SWITCH));
      await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
    });

    it("does not warn while the actions setting is still loading", () => {
      getEventSettings.mockReturnValue(new Promise(() => undefined));
      renderTab({ ...TRIGGER, httpEnabled: false, workflowEnabled: false });

      expect(screen.queryByRole("alert")).toBeNull();
    });
  });
});
