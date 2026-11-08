import { NodeGuideTriggerBlocksEventV2 } from "../node-guides";
import { OPEN_IN_OS_ACTIONS_PATH } from "@/modules/open-in-os/constants";
import { actionsService } from "@/modules/actions/services/actions.service";
import { IActionOption } from "@/modules/actions/types/actions.type";
import { actionOptionLabel, payloadHint } from "@/modules/actions/utils/action-options";
import { NodeSchemaDefinition } from "./node-schema.type";
import { ReadonlyDetails, SelectOption } from "../node-inspector/form-builder/form-field.types";
import { tStatic } from "@seliseblocks/genesis-os";

const LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE = "logic.workflow.nodes.schemas";

const t = (key: string, options?: Readonly<Record<string, string | number>>) =>
  tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, `NODE_SCHEMA_TRIGGER_ACTION_V2.${key}`, options);

/** The Action select and the status line ask at the same moment; one request serves both. */
const SHARED_FETCH_MS = 1500;
let sharedFetch: { at: number; promise: Promise<IActionOption[]> } | null = null;

/** The actions turned on for workflows (`Actions/GetForWorkflow`). */
export const loadWorkflowActions = (): Promise<IActionOption[]> => {
  const now = Date.now();
  if (sharedFetch && now - sharedFetch.at < SHARED_FETCH_MS) return sharedFetch.promise;
  const promise = actionsService.getForWorkflow().then((res) => res ?? []);
  sharedFetch = { at: now, promise };
  promise.catch(() => {
    sharedFetch = null;
  });
  return promise;
};

/** Test hook: forget the shared request. */
export const resetWorkflowActionsCache = () => {
  sharedFetch = null;
};

/**
 * The actions on for workflows as `{Label} ({name})`, with the data fields as the description. A saved action that is no
 * longer on stays selectable as its own option, so the select keeps the value instead of resetting it.
 */
export const buildActionOptions = (actions: IActionOption[], savedActionName: string): SelectOption[] => {
  const options: SelectOption[] = actions.map((action) => ({
    value: action.name,
    label: actionOptionLabel(action),
    description: payloadHint(action),
  }));
  if (savedActionName && !actions.some((action) => action.name === savedActionName)) {
    options.unshift({ value: savedActionName, label: t("NOT_ON_FOR_WORKFLOWS", { savedActionName }) });
  }
  return options;
};

/** The status line under the select: the selected action's data fields, a warning when it is off, a hint when none is on. */
export const buildActionStatus = (actions: IActionOption[], savedActionName: string): ReadonlyDetails => {
  const selected = actions.find((action) => action.name === savedActionName);
  if (savedActionName && !selected) return { fields: [], message: t("OFF_WARNING") };
  if (actions.length === 0) return { fields: [], message: t("NO_ACTIONS_ON") };
  const fields = payloadHint(selected);
  return fields ? { fields: [], message: t("DATA_FIELDS", { fields }) } : { fields: [] };
};

/** The node's saved action name (kept in `eventName`); anything that is not a string counts as none. */
export const actionNameOf = (data: Record<string, unknown>): string =>
  typeof data.eventName === "string" ? data.eventName : "";

/** The Action trigger. The type and the `eventName` key are the old Blocks Event node's, so saved workflows still load. */
export const NodeSchemaTriggerBlocksEventV2: NodeSchemaDefinition = {
  guide: NodeGuideTriggerBlocksEventV2,
  schema: {
    type: "blocksEvent",
    category: "trigger",
    version: "v2",
    parameters: [
      {
        id: "event",
        type: "select",
        get label() { return t("ACTION"); },
        get info() { return t("THE_ACTION_THAT"); },
        key: "eventName",
        required: true,
        searchable: true,
        get placeholder() { return t("SELECT_AN_ACTION"); },
        // An admin may turn actions on in Blocks OS meanwhile; returning here picks the change up.
        refetchOnWindowFocus: true,
        options: (data) => loadWorkflowActions().then((actions) => buildActionOptions(actions, actionNameOf(data))),
      },
      {
        // Opens this project's Actions page in Blocks OS in a new tab (through a Logic page that signs in via IAM).
        // Shown in every state, even when the actions cannot be loaded. Display only: nothing is saved on the node.
        id: "configure-actions-link",
        type: "link",
        key: "configureActionsLink",
        transient: true,
        linkPath: OPEN_IN_OS_ACTIONS_PATH,
        get label() { return t("CONFIGURE_ACTIONS_IN_OS"); },
        get ariaLabel() { return t("CONFIGURE_ACTIONS_IN_OS_NEW_TAB"); },
      },
      {
        id: "eventStatus",
        type: "readonly-details",
        key: "eventStatus",
        // Display only: nothing is saved on the node.
        transient: true,
        detailsDependencies: ["eventName"],
        details: (data) => loadWorkflowActions().then((actions) => buildActionStatus(actions, actionNameOf(data))),
      },
      {
        id: "output",
        type: "display",
        get label() { return t("OUTPUT"); },
        get info() { return t("STRUCTURE_OF_THE_TRIGGER"); },
        key: "output",
        displayValue: (data) => `\`\`\`json
{
  "id": String,
  "action": "${actionNameOf(data) || "action.name"}",
  "time": String,
  "organizationId": String | null,
  "data": { ...the action's own fields }
}
\`\`\`
In the next node read a field with \`{{$json.data.userId}}\` or \`{{$json.organizationId}}\`.`,
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
