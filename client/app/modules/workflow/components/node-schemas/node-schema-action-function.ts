import React from "react";
import { functionService } from "@blocks-functions/services/function.service";
import { NodeGuideActionFunction } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { SelectOption } from "../node-inspector/form-builder/form-field.types";
import { tStatic } from "@seliseblocks/genesis-os";
import { renderRichText } from "@/lib/rich-text";

const LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE = "logic.workflow.nodes.schemas";

/** One page is enough for a picker; the list is sorted by name so it is scannable. */
const FUNCTION_PICKER_PAGE_SIZE = 200;

const CODE_CLASS = "bg-muted px-1.5 py-0.5 rounded-md text-sm font-mono text-primary font-semibold";
const code = (text: string) => React.createElement("code", { className: CODE_CLASS }, text);

const INPUT_JSON_PLACEHOLDER = `{
  "orderId": {{$json.orderId}},
  "email": "{{$json.customer.email}}",
  "source": "workflow"
}`;

/**
 * The deployed functions this step can actually call.
 *
 * A function whose workflow trigger is off is still listed, disabled and labelled with the
 * reason, rather than hidden: the server rejects it with "this function cannot be invoked from
 * a workflow", and a function that silently never appears in the list is the harder version of
 * that message to act on. Keeping it in the list also matters for a node that already points at
 * it — `SelectField` clears any stored value it cannot find among the options, so filtering it
 * out would wipe the selection just by opening the node.
 *
 * A failed fetch yields an empty list rather than a rejected promise: the field would otherwise
 * leave an unhandled rejection behind, and `SelectField` treats an empty list as "nothing to
 * reconcile" and leaves the saved selection alone.
 */
const loadFunctionOptions = async (): Promise<SelectOption[]> => {
  try {
    const response = await functionService.getFunctions({
      status: "Live",
      sortBy: "Name",
      pageNumber: 0,
      pageSize: FUNCTION_PICKER_PAGE_SIZE,
    });

    return (response.data ?? []).map((fn) => ({
      value: fn.id,
      label: fn.workflowEnabled ? fn.name : tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_FUNCTION.WORKFLOW_TRIGGER_OFF", { name: fn.name }),
      disabled: !fn.workflowEnabled,
    }));
  } catch {
    return [];
  }
};

export const NodeSchemaActionFunction: NodeSchemaDefinition = {
  guide: NodeGuideActionFunction,
  schema: {
    type: "function",
    category: "action",
    version: "v1",
    parameters: [
      {
        id: "functionId",
        type: "select",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_FUNCTION.FUNCTION"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_FUNCTION.THE_DEPLOYED_FUNCTION_THIS"); },
        key: "functionId",
        get placeholder() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_FUNCTION.SELECT_A_DEPLOYED_FUNCTION"); },
        required: true,
        searchable: true,
        options: loadFunctionOptions,
      },
      {
        id: "inputMode",
        type: "select",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_FUNCTION.INPUT"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_FUNCTION.WHAT_THE_FUNCTION_RECEIVES"); },
        key: "inputMode",
        options: [
          { get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_FUNCTION.PREVIOUS_STEP_S_OUTPUT"); }, value: "item" },
          { get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_FUNCTION.CUSTOM_JSON"); }, value: "expression" },
        ],
      },
      {
        id: "input-json-notes",
        type: "callout-accordion-display",
        key: "inputJsonNotes",
        // Display-only. Without this, switching the mode would cascade a `null` for this key
        // into the node's saved parameters (cascadeFieldResets writes every dependent field).
        transient: true,
        dependsOn: { key: "inputMode", value: "expression" },
        displayValue: () => ({
          title: tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_FUNCTION.HOW_TO_BUILD_THE"),
          description: React.createElement("span", null, ...renderRichText(tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_FUNCTION.WRITE_THE_JSON_THE"), [code, code, code, code, code])),
        }),
      },
      {
        id: "inputExpression",
        type: "json-code-editor",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_FUNCTION.INPUT_JSON"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_FUNCTION.THE_PAYLOAD_HANDED_TO"); },
        key: "inputExpression",
        placeholder: INPUT_JSON_PLACEHOLDER,
        height: 168,
        dependsOn: { key: "inputMode", value: "expression" },
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      functionId: "",
      inputMode: "item",
      inputExpression: "",
    },
    settings: {},
  },
  transform: (node) => node,
};
