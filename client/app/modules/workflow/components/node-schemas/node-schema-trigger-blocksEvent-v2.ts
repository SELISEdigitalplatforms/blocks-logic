import React from "react";
import { NodeGuideTriggerBlocksEventV2 } from "../node-guides";
import { eventTriggersService } from "@/modules/event-triggers/services/event-triggers.service";
import { IBlocksEventWorkflowOption } from "@/modules/event-triggers/types/event-triggers.type";
import { ManageBlocksEventsLink } from "@/modules/event-triggers/components/manage-blocks-events-link/manage-blocks-events-link";
import { NodeSchemaDefinition } from "./node-schema.type";
import { ReadonlyDetails, SelectOption } from "../node-inspector/form-builder/form-field.types";
import { tStatic } from "@seliseblocks/genesis-os";
import { renderRichText } from "@/lib/rich-text";

const LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE = "logic.workflow.nodes.schemas";

export const DISABLED_EVENT_WARNING =
  "This event is disabled for workflows. Enable it with Manage events. The published workflow will not run until then.";
export const NO_ENABLED_EVENTS_MESSAGE = "No events are enabled for workflows. Use Manage events to enable one.";

/** The Event select and the status line ask at the same moment; one request serves both. */
const SHARED_FETCH_MS = 1500;
let sharedFetch: { at: number; promise: Promise<IBlocksEventWorkflowOption[]> } | null = null;

export const loadWorkflowEvents = (): Promise<IBlocksEventWorkflowOption[]> => {
  const now = Date.now();
  if (sharedFetch && now - sharedFetch.at < SHARED_FETCH_MS) return sharedFetch.promise;
  const promise = eventTriggersService.getWorkflowEvents().then((res) => res?.items ?? []);
  sharedFetch = { at: now, promise };
  promise.catch(() => {
    sharedFetch = null;
  });
  return promise;
};

/** Test hook: forget the shared request. */
export const resetWorkflowEventsCache = () => {
  sharedFetch = null;
};

/**
 * The enabled events as `{Label} ({EventName})`. A saved event that is no longer enabled stays selectable as
 * its own option, so the select keeps the value instead of resetting it (the status line explains why).
 */
export const buildEventOptions = (
  events: IBlocksEventWorkflowOption[],
  savedEventName: string,
): SelectOption[] => {
  const options: SelectOption[] = events.map((event) => ({
    value: event.eventName,
    label: `${event.label} (${event.eventName})`,
    description: event.description,
  }));
  if (savedEventName && !events.some((event) => event.eventName === savedEventName)) {
    options.unshift({ value: savedEventName, label: tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_BLOCKS_EVENT_V2.DISABLED_FOR_WORKFLOWS", { savedEventName }) });
  }
  return options;
};

/** The status line under the select: a warning for a disabled saved event, a hint when nothing is enabled. */
export const buildEventStatus = (events: IBlocksEventWorkflowOption[], savedEventName: string): ReadonlyDetails => {
  if (savedEventName && !events.some((event) => event.eventName === savedEventName)) {
    return { fields: [], message: DISABLED_EVENT_WARNING };
  }
  if (events.length === 0) return { fields: [], message: NO_ENABLED_EVENTS_MESSAGE };
  return { fields: [] };
};

/** The node's saved event name; anything that is not a string counts as none. */
export const eventNameOf = (data: Record<string, unknown>): string =>
  typeof data.eventName === "string" ? data.eventName : "";

const codeClass = "bg-muted px-1.5 py-0.5 rounded-md text-sm font-mono text-primary font-semibold";

export const NodeSchemaTriggerBlocksEventV2: NodeSchemaDefinition = {
  guide: NodeGuideTriggerBlocksEventV2,
  schema: {
    type: "blocksEvent",
    category: "trigger",
    version: "v2",
    parameters: [
      {
        id: "execution-notes",
        type: "callout-accordion-display",
        key: "executionNotes",
        displayValue: () => ({
          title: tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_BLOCKS_EVENT_V2.NOTES"),
          description: React.createElement(
            "span",
            null,
            ...renderRichText(tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_BLOCKS_EVENT_V2.RUNS_THE"), [(text: string) => React.createElement("code", { className: codeClass }, text)]),
            " ",
            React.createElement(ManageBlocksEventsLink),
          ),
        }),
      },
      {
        id: "event",
        type: "select",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_BLOCKS_EVENT_V2.EVENT"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_BLOCKS_EVENT_V2.THE_PLATFORM_EVENT_THAT"); },
        key: "eventName",
        required: true,
        searchable: true,
        get placeholder() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_BLOCKS_EVENT_V2.SELECT_AN_EVENT"); },
        // The list can change in the Event Triggers tab; returning here picks the change up.
        refetchOnWindowFocus: true,
        options: (data) => loadWorkflowEvents().then((events) => buildEventOptions(events, eventNameOf(data))),
      },
      {
        id: "eventStatus",
        type: "readonly-details",
        key: "eventStatus",
        // Display only: nothing is saved on the node.
        transient: true,
        detailsDependencies: ["eventName"],
        details: (data) => loadWorkflowEvents().then((events) => buildEventStatus(events, eventNameOf(data))),
      },
      {
        id: "output",
        type: "display",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_BLOCKS_EVENT_V2.OUTPUT"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_BLOCKS_EVENT_V2.STRUCTURE_OF_THE_TRIGGER"); },
        key: "output",
        displayValue: (data) => `\`\`\`json
{
  "EventId": String,
  "EventName": "${eventNameOf(data) || "EventName"}",
  "SourceService": String,
  "ConsumerName": String,
  "IsMassConsumer": Boolean,
  "Timestamp": String,
  "Payload": { ...the event's own fields }
}
\`\`\`
\`Payload\` is the event as the sending service published it. A payload that is not a JSON object is passed on as text.`,
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      eventName: "",
    },
    settings: {},
  },
  transform: (node) => node,
};
