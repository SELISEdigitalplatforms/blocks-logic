import { act } from "react";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { eventTriggersService } from "@/modules/event-triggers/services/event-triggers.service";
import { IBlocksEventWorkflowOption } from "@/modules/event-triggers/types/event-triggers.type";
import { NodeDefinitions } from "../node-library-panel/node-definitions";
import { SelectField, REFETCH_ON_FOCUS_THROTTLE_MS } from "../node-inspector/form-builder/fields/select-field";
import { NodeSchemasDefinition } from "./node-schemas";
import {
  DISABLED_EVENT_WARNING,
  NO_ENABLED_EVENTS_MESSAGE,
  NodeSchemaTriggerBlocksEventV2,
  buildEventOptions,
  buildEventStatus,
  resetWorkflowEventsCache,
} from "./node-schema-trigger-blocksEvent-v2";

vi.mock("@seliseblocks/genesis-os", async () => {
  const actual = await vi.importActual("@seliseblocks/genesis-os");
  return { ...actual, useScopedPath: () => (path: string) => `/app/item-123/${path}` };
});

const EVENTS: IBlocksEventWorkflowOption[] = [
  { eventName: "UserMutationEvent", label: "User changed", serviceKey: "iam", description: "A user was created or updated." },
  { eventName: "SmsStatusEvent", label: "SMS status", serviceKey: "utilities", description: "An SMS delivery status changed." },
];

const param = (id: string) => NodeSchemaTriggerBlocksEventV2.schema.parameters.find((p) => p.id === id)!;

describe("Blocks Event node", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    resetWorkflowEventsCache();
  });

  it("H1: the node library lists a Blocks Event trigger with the Zap card", () => {
    const definition = NodeDefinitions.find((d) => d.type === "blocksEvent")!;
    expect(definition).toBeTruthy();
    expect(definition.category).toBe("trigger");
    expect(definition.title).toBe("Blocks Event");
    expect(definition.description).toBe("Starts the workflow when a Blocks service sends the selected event.");
    expect(definition.handle).toEqual({ sources: [{ key: "source", label: "" }], targets: [] });
  });

  it("the schema is registered as v2 only, like the other new types", () => {
    expect(NodeSchemasDefinition.triggerblocksEventv2).toBe(NodeSchemaTriggerBlocksEventV2);
    expect(NodeSchemasDefinition.triggerblocksEventv1).toBeUndefined();
    expect(NodeSchemaTriggerBlocksEventV2.defaults).toEqual({ parameters: { eventName: "" }, settings: {} });
  });

  it("H2: options are the enabled events, labelled '{Label} ({EventName})' with the name as value", async () => {
    vi.spyOn(eventTriggersService, "getWorkflowEvents").mockResolvedValue({ items: EVENTS });
    const options = param("event").options as (d: Record<string, unknown>) => Promise<{ value: unknown; label: string }[]>;

    const result = await options({ eventName: "" });

    expect(result.map((o) => [o.value, o.label])).toEqual([
      ["UserMutationEvent", "User changed (UserMutationEvent)"],
      ["SmsStatusEvent", "SMS status (SmsStatusEvent)"],
    ]);
    expect(param("event").refetchOnWindowFocus).toBe(true);
    expect(param("event").required).toBe(true);
  });

  it("C13: a saved event that is no longer enabled stays selectable and shows the warning", () => {
    const options = buildEventOptions(EVENTS, "PaymentLifecycleEvent");
    expect(options[0]).toEqual({ value: "PaymentLifecycleEvent", label: "PaymentLifecycleEvent (disabled for workflows)" });
    expect(buildEventStatus(EVENTS, "PaymentLifecycleEvent").message).toBe(DISABLED_EVENT_WARNING);
    expect(buildEventStatus(EVENTS, "UserMutationEvent").message).toBeUndefined();
  });

  it("C12: with nothing enabled the node says how to enable one", () => {
    expect(buildEventOptions([], "")).toEqual([]);
    expect(buildEventStatus([], "").message).toBe(NO_ENABLED_EVENTS_MESSAGE);
  });

  it("the status line is display-only and follows the selected event", () => {
    const status = param("eventStatus");
    expect(status.type).toBe("readonly-details");
    expect(status.transient).toBe(true);
    expect(status.detailsDependencies).toEqual(["eventName"]);
  });

  it("the select and the status line share one request", async () => {
    const spy = vi.spyOn(eventTriggersService, "getWorkflowEvents").mockResolvedValue({ items: EVENTS });
    const options = param("event").options as (d: Record<string, unknown>) => Promise<unknown>;
    const details = param("eventStatus").details!;

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await Promise.all([options({ eventName: "" }), details({ eventName: "" }, {} as any)]);

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
    renderWithProviders(
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
    return { load, onChange };
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

  it("C18: a select without the flag never reloads on focus", async () => {
    const { load } = renderSelect(undefined);
    await act(async () => {});

    await focus();
    vi.setSystemTime(Date.now() + 10_000);
    await focus();

    expect(load).toHaveBeenCalledTimes(1);
  });
});
