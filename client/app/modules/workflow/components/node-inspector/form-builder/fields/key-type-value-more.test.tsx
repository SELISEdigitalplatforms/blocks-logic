import React from "react";
import { fireEvent, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { KeyTypeValueField } from "./key-type-value-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const cfg: any = { projectKey: "pk", workflowId: "wf", nodeId: "n1" };
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const field = (extra: Record<string, unknown> = {}): any => ({
  id: "ktv",
  key: "fields",
  type: "key-type-value-pairs",
  ...extra,
});

afterEach(() => {
  document.body.style.pointerEvents = "";
});

describe("KeyTypeValueField", () => {
  it("renders the empty add prompt and adds a pair", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <KeyTypeValueField
        field={field()}
        value={[]}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    fireEvent.click(screen.getByText("Add Field"));
    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("removes a pair", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <KeyTypeValueField
        field={field()}
        value={[{ key: "k", type: "string", value: "v" }]}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[0]);
    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("switches a pair to the array type and edits chips", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(
      <KeyTypeValueField
        field={field()}
        value={[{ key: "tags", type: "string", value: "" }]}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    // open the type Select and pick Array
    await user.click(screen.getByRole("combobox"));
    await user.click(await screen.findByText("Array"));
    expect(onChange).toHaveBeenCalledWith([
      expect.objectContaining({ type: "array", value: [] }),
    ]);
    // the chips input renders for array types
    const chipInput = screen.getByPlaceholderText("Type and press enter");
    fireEvent.change(chipInput, { target: { value: "one" } });
    fireEvent.keyDown(chipInput, { key: "Enter" });
    expect(onChange).toHaveBeenCalledWith([
      expect.objectContaining({ value: ["one"] }),
    ]);
  });

  it("edits a non-array value through the expression input", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <KeyTypeValueField
        field={field()}
        value={[{ key: "k", type: "string", value: "" }]}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    const inputs = document.querySelectorAll("input");
    // the value expression input is the last input
    fireEvent.change(inputs[inputs.length - 1], { target: { value: "hi" } });
    expect(onChange).toHaveBeenCalledWith([
      expect.objectContaining({ value: "hi" }),
    ]);
  });
});
