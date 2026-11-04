import { act } from "react";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { actionsService } from "@/modules/actions/services/actions.service";
import { IActionOption } from "@/modules/actions/types/actions.type";
import { NodeDefinitions } from "../node-library-panel/node-definitions";
import { SelectField, REFETCH_ON_FOCUS_THROTTLE_MS } from "../node-inspector/form-builder/fields/select-field";
import { NodeSchemasDefinition } from "./node-schemas";
import {
  NodeSchemaTriggerBlocksEventV2,
  actionNameOf,
  buildActionOptions,
  buildActionStatus,
  resetWorkflowActionsCache,
} from "./node-schema-trigger-blocksEvent-v2";

const USER_CREATED = "iam.user.created";
const USER_DEACTIVATED = "iam.user.deactivated";
const USER_UPDATED = "iam.user.updated";
const ACTIONS: IActionOption[] = [
  { name: USER_CREATED, label: "User created", service: "iam", payload: { userId: "string", roles: "string[]" } },
  { name: USER_DEACTIVATED, label: "User deactivated", service: "iam", payload: {} },
];

const param = (id: string) => NodeSchemaTriggerBlocksEventV2.schema.parameters.find((p) => p.id === id)!;

describe("Action trigger node", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    resetWorkflowActionsCache();
  });

  it("the node library lists an Action trigger; the type stays blocksEvent so saved workflows load", () => {
    const definition = NodeDefinitions.find((d) => d.type === "blocksEvent")!;
    expect(definition.category).toBe("trigger");
    expect(definition.title).toBe("Action");
    expect(definition.handle).toEqual({ sources: [{ key: "source", label: "" }], targets: [] });
  });

  it("the schema is registered as v2 only and still saves the action in eventName", () => {
    expect(NodeSchemasDefinition.triggerblocksEventv2).toBe(NodeSchemaTriggerBlocksEventV2);
    expect(NodeSchemasDefinition.triggerblocksEventv1).toBeUndefined();
    expect(NodeSchemaTriggerBlocksEventV2.defaults).toEqual({ parameters: { eventName: "" }, settings: {} });
  });

  it("the picker lists only the actions on for workflows, as '{label} ({name})', searchable, with the data fields", async () => {
    const spy = vi.spyOn(actionsService, "getForWorkflow").mockResolvedValue(ACTIONS);
    const options = param("event").options as (d: Record<string, unknown>) => Promise<{ value: unknown; label: string; description?: string }[]>;

    const result = await options({ eventName: "" });

    expect(spy).toHaveBeenCalledTimes(1);
    expect(result.map((o) => [o.value, o.label])).toEqual([
      [USER_CREATED, "User created (iam.user.created)"],
      [USER_DEACTIVATED, "User deactivated (iam.user.deactivated)"],
    ]);
    expect(result[0].description).toBe("userId (string), roles (string[])");
    expect(param("event").searchable).toBe(true);
    expect(param("event").required).toBe(true);
  });

  it("a saved action that is no longer on stays selectable and shows the warning", () => {
    const options = buildActionOptions(ACTIONS, USER_UPDATED);
    expect(options[0]).toEqual({ value: USER_UPDATED, label: "iam.user.updated (not on for workflows)" });
    expect(buildActionStatus(ACTIONS, USER_UPDATED).message).toContain("not on for workflows");
  });

  it("the status line shows the selected action's data fields", () => {
    expect(buildActionStatus(ACTIONS, USER_CREATED).message).toBe("Data fields: userId (string), roles (string[])");
    expect(buildActionStatus(ACTIONS, USER_DEACTIVATED).message).toBeUndefined();
  });

  it("with nothing on the node says where an admin turns actions on", () => {
    expect(buildActionOptions([], "")).toEqual([]);
    expect(buildActionStatus([], "").message).toContain("Blocks OS");
  });

  it("reads the saved action name only when it is a string", () => {
    expect(actionNameOf({ eventName: USER_CREATED })).toBe(USER_CREATED);
    expect(actionNameOf({ eventName: { nested: true } })).toBe("");
    expect(actionNameOf({})).toBe("");
  });

  it("the output preview is the simple action json", () => {
    const display = param("output").displayValue!;
    const text = display({ eventName: USER_CREATED }, {} as Parameters<typeof display>[1]) as string;
    expect(text).toContain('"action": "iam.user.created"');
    expect(text).toContain('"organizationId"');
    expect(text).toContain('"data"');
  });

  it("the select and the status line share one request", async () => {
    const spy = vi.spyOn(actionsService, "getForWorkflow").mockResolvedValue(ACTIONS);
    const options = param("event").options as (d: Record<string, unknown>) => Promise<unknown>;
    const details = param("eventStatus").details!;

    await Promise.all([options({ eventName: "" }), details({ eventName: "" }, {} as Parameters<typeof details>[1])]);

    expect(spy).toHaveBeenCalledTimes(1);
  });
});

describe("SelectField refetchOnWindowFocus", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const cfg: any = { tenantId: "pk", workflowId: "wf", nodeId: "n1" };

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-10-31T10:00:00Z"));
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  const renderSelect = (refetchOnWindowFocus?: boolean) => {
    const load = vi.fn().mockResolvedValue([{ value: "A", label: "A" }]);
    const onChange = vi.fn();
    const view = renderWithProviders(
      <SelectField
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        field={{ id: "sel", key: "sel", type: "select", options: load, refetchOnWindowFocus } as any}
        value="A"
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    return { load, onChange, unmount: view.unmount };
  };

  const focus = async () => {
    await act(async () => {
      window.dispatchEvent(new Event("focus"));
    });
  };

  it("H11: reloads the options when the window regains focus, at most once per 2 s, keeping the value", async () => {
    const { load, onChange } = renderSelect(true);
    await act(async () => {});
    expect(load).toHaveBeenCalledTimes(1);

    await focus();
    expect(load).toHaveBeenCalledTimes(2);

    await focus();
    expect(load).toHaveBeenCalledTimes(2);

    vi.setSystemTime(Date.now() + REFETCH_ON_FOCUS_THROTTLE_MS);
    await focus();
    expect(load).toHaveBeenCalledTimes(3);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("H11: the tab becoming visible again also reloads, and a hidden tab does not", async () => {
    const { load } = renderSelect(true);
    await act(async () => {});
    const setVisibility = async (state: DocumentVisibilityState) => {
      Object.defineProperty(document, "visibilityState", { configurable: true, get: () => state });
      await act(async () => {
        document.dispatchEvent(new Event("visibilitychange"));
      });
    };

    await setVisibility("hidden");
    expect(load).toHaveBeenCalledTimes(1);
    await setVisibility("visible");
    expect(load).toHaveBeenCalledTimes(2);
  });

  it("a failed reload keeps the options already shown", async () => {
    const { load, onChange } = renderSelect(true);
    await act(async () => {});
    load.mockRejectedValueOnce(new Error("offline"));

    await focus();

    expect(load).toHaveBeenCalledTimes(2);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("stops listening once unmounted", async () => {
    const { load, unmount } = renderSelect(true);
    await act(async () => {});
    unmount();

    await focus();

    expect(load).toHaveBeenCalledTimes(1);
  });

  it("C18: a select without the flag never reloads on focus", async () => {
    const { load } = renderSelect(undefined);
    await act(async () => {});

    await focus();
    vi.setSystemTime(Date.now() + 10_000);
    await focus();

    expect(load).toHaveBeenCalledTimes(1);
  });
});
