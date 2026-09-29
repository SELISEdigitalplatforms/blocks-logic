import React from "react";
import { functionService } from "@blocks-functions/services/function.service";
import { NodeGuideActionFunction } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { SelectOption } from "../node-inspector/form-builder/form-field.types";

/** One page is enough for a picker; the list is sorted by name so it is scannable. */
const FUNCTION_PICKER_PAGE_SIZE = 200;

/**
 * Longest wait the step may be configured for. Mirrors the server's `Functions:SyncWaitMaxSeconds`
 * (FunctionInvocationService.DefaultSyncWaitMaxSeconds): anything above it is clamped there
 * anyway, so offering it here would only promise a wait the server will not honour.
 */
export const FUNCTION_STEP_MAX_WAIT_SECONDS = 180;

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
      label: fn.workflowEnabled ? fn.name : `${fn.name} — workflow trigger off`,
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
        label: "Function",
        info:
          "The deployed function this step runs. Only Live functions are listed; one whose workflow trigger is turned off is shown greyed out until that trigger is enabled in the function's settings.",
        key: "functionId",
        placeholder: "Select a deployed function",
        required: true,
        searchable: true,
        options: loadFunctionOptions,
      },
      {
        id: "inputMode",
        type: "select",
        label: "Input",
        info:
          "What the function receives as its input. \"Previous step's output\" forwards the incoming item as-is; \"Custom JSON\" lets you compose the payload, pulling values from earlier steps.",
        key: "inputMode",
        options: [
          { label: "Previous step's output", value: "item" },
          { label: "Custom JSON", value: "expression" },
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
          title: "How to build the input",
          description: React.createElement(
            "span",
            null,
            "Write the JSON the function should receive. Insert values from earlier steps with ",
            code("{{$json.field}}"),
            " for the incoming item, ",
            code('{{$node["Step name"].json.output.field}}'),
            " for any earlier step, or ",
            code("{{$context.key}}"),
            " for workflow context. Wrap text values in quotes (",
            code('"{{$json.email}}"'),
            "); leave numbers, booleans and objects unquoted. When testing this step on its own with nothing connected, the incoming item is empty.",
          ),
        }),
      },
      {
        id: "inputExpression",
        type: "json-code-editor",
        label: "Input JSON",
        info: "The payload handed to the function as its input. Placeholders in {{ }} are resolved when the step runs.",
        key: "inputExpression",
        placeholder: INPUT_JSON_PLACEHOLDER,
        height: 168,
        dependsOn: { key: "inputMode", value: "expression" },
      },
      {
        id: "waitTimeoutSec",
        type: "number",
        label: "Wait timeout (seconds)",
        info:
          "How long this step waits for the function to finish, from 1 to 180 seconds. Leave empty to use the function's own timeout plus a short grace period. If the run is still going when the wait ends, the step fails.",
        key: "waitTimeoutSec",
        placeholder: "Function's own timeout",
        min: 1,
        max: FUNCTION_STEP_MAX_WAIT_SECONDS,
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      functionId: "",
      inputMode: "item",
      inputExpression: "",
      waitTimeoutSec: null,
    },
    settings: {},
  },
  transform: (node) => node,
};
