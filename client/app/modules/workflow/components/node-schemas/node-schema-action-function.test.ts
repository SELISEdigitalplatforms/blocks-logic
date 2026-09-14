import { beforeEach, describe, expect, it, vi } from "vitest";

const { getFunctions } = vi.hoisted(() => ({ getFunctions: vi.fn() }));

vi.mock("@blocks-functions/services/function.service", () => ({
  functionService: { getFunctions },
}));

import {
  FUNCTION_STEP_MAX_WAIT_SECONDS,
  NodeSchemaActionFunction,
} from "./node-schema-action-function";

const field = (key: string) => NodeSchemaActionFunction.schema.parameters.find((p) => p.key === key)!;

beforeEach(() => {
  vi.clearAllMocks();
});

describe("NodeSchemaActionFunction", () => {
  it("declares the function node's type/category/version", () => {
    expect(NodeSchemaActionFunction.schema.type).toBe("function");
    expect(NodeSchemaActionFunction.schema.category).toBe("action");
    expect(NodeSchemaActionFunction.schema.version).toBe("v1");
  });

  it("defaults to item input mode with no function selected", () => {
    expect(NodeSchemaActionFunction.defaults.parameters).toEqual({
      functionId: "",
      inputMode: "item",
      inputExpression: "",
      waitTimeoutSec: null,
    });
  });

  it("requires a function to be selected", () => {
    expect(field("functionId").required).toBe(true);
  });

  it("only shows the input JSON editor and its help when inputMode is expression", () => {
    expect(field("inputExpression").dependsOn).toEqual({ key: "inputMode", value: "expression" });
    expect(field("inputJsonNotes").dependsOn).toEqual({ key: "inputMode", value: "expression" });
  });

  it("presents the custom input as a JSON editor with a worked placeholder", () => {
    // A payload, not a one-line expression: the same editor the Proxy and HTTP nodes use for a
    // body, so it is multi-line, validates the JSON and highlights {{ }} placeholders.
    const editor = field("inputExpression");
    expect(editor.type).toBe("json-code-editor");
    expect(editor.placeholder).toContain("{{$json.");
    expect(editor.info).toBeTruthy();
  });

  it("explains the placeholder syntax in an expandable note", () => {
    const notes = field("inputJsonNotes");
    expect(notes.type).toBe("callout-accordion-display");
    const value = notes.displayValue!({}, {} as never) as { title: string; description: unknown };
    expect(value.title).toBe("How to build the input");
    expect(value.description).toBeDefined();
    // Never persisted: a mode change cascades defaults into every dependent field.
    expect(notes.transient).toBe(true);
  });

  it("gives every user-facing field a tooltip", () => {
    for (const key of ["functionId", "inputMode", "inputExpression", "waitTimeoutSec"]) {
      expect(field(key).info, key).toBeTruthy();
    }
  });

  it("caps the wait timeout at the server's sync ceiling", () => {
    // Functions:SyncWaitMaxSeconds is 180 s; the editor must not offer a wait the server clamps.
    expect(FUNCTION_STEP_MAX_WAIT_SECONDS).toBe(180);
    expect(field("waitTimeoutSec").min).toBe(1);
    expect(field("waitTimeoutSec").max).toBe(FUNCTION_STEP_MAX_WAIT_SECONDS);
    expect(field("waitTimeoutSec").info).toContain("180");
  });

  it("labels the input modes by what the function receives", () => {
    expect(field("inputMode").options).toEqual([
      { label: "Previous step's output", value: "item" },
      { label: "Custom JSON", value: "expression" },
    ]);
  });

  const loadOptions = () =>
    (field("functionId").options as (data: unknown, config: unknown) => Promise<unknown>)({}, {});

  it("loads only Live functions for the function picker, mapped to id/name options", async () => {
    getFunctions.mockResolvedValue({
      data: [
        { id: "fn_1", name: "Send confirmation", workflowEnabled: true },
        { id: "fn_2", name: "Sync inventory", workflowEnabled: true },
      ],
      totalCount: 2,
    });

    const options = await loadOptions();

    expect(getFunctions).toHaveBeenCalledWith(
      expect.objectContaining({ status: "Live" }),
    );
    expect(options).toEqual([
      { value: "fn_1", label: "Send confirmation", disabled: false },
      { value: "fn_2", label: "Sync inventory", disabled: false },
    ]);
  });

  it("offers a function whose workflow trigger is off as a disabled, labelled option", async () => {
    // The server rejects it with "this function cannot be invoked from a workflow", so picking it
    // could only ever fail at run time — but hiding it outright would also wipe the selection of a
    // node already pointing at it, since SelectField drops a value it cannot find in the options.
    getFunctions.mockResolvedValue({
      data: [{ id: "fn_3", name: "Nightly rollup", workflowEnabled: false }],
      totalCount: 1,
    });

    const options = await loadOptions();

    expect(options).toEqual([
      { value: "fn_3", label: "Nightly rollup — workflow trigger off", disabled: true },
    ]);
  });

  it("yields an empty list when the functions cannot be loaded, instead of rejecting", async () => {
    // SelectField has no rejection path: an unhandled rejection is all the user would get, and an
    // empty list is the one result that leaves an already-saved selection untouched.
    getFunctions.mockRejectedValue(new Error("network down"));

    await expect(loadOptions()).resolves.toEqual([]);
  });

  it("tolerates a response with no data", async () => {
    getFunctions.mockResolvedValue({ data: null, totalCount: 0 });

    await expect(loadOptions()).resolves.toEqual([]);
  });

  it("has an identity transform and a guide, like every other node schema", () => {
    expect(typeof NodeSchemaActionFunction.transform).toBe("function");
    const node = { parameters: { functionId: "fn_1" } };
    expect(NodeSchemaActionFunction.transform!(node as never)).toBe(node);
    expect(NodeSchemaActionFunction.guide).toBeDefined();
  });
});
