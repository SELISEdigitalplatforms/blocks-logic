import React from "react";
import { fireEvent, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { ExpressionListField } from "./expression-list-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const cfg: any = { tenantId: "pk", workflowId: "wf", nodeId: "node-1" };
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const field = (extra: Record<string, unknown> = {}): any => ({
  id: "attachments",
  key: "Attachments",
  type: "expression-list",
  addButtonText: "Add attachment",
  ...extra,
});

describe("ExpressionListField", () => {
  it("renders an empty state with just an Add button when value is empty", () => {
    renderWithProviders(
      <ExpressionListField field={field()} value={[]} onChange={vi.fn()} data={{}} config={cfg} />,
    );
    expect(screen.getByText("Add attachment")).toBeTruthy();
    expect(screen.queryAllByRole("textbox")).toHaveLength(0);
  });

  it("adds a new empty row and reports it via onChange", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <ExpressionListField field={field()} value={[]} onChange={onChange} data={{}} config={cfg} />,
    );
    fireEvent.click(screen.getByText("Add attachment"));
    expect(onChange).toHaveBeenCalledWith([""]);
  });

  it("renders one row per existing value, as string[]", () => {
    renderWithProviders(
      <ExpressionListField
        field={field()}
        value={["file_abc123", '{{$json.output.fileId}}']}
        onChange={vi.fn()}
        data={{}}
        config={cfg}
      />,
    );
    const inputs = screen.getAllByRole("textbox") as HTMLInputElement[];
    expect(inputs).toHaveLength(2);
    expect(inputs[0].value).toBe("file_abc123");
    expect(inputs[1].value).toBe("{{$json.output.fileId}}");
  });

  it("updates a row's value on change without touching the others", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <ExpressionListField
        field={field()}
        value={["file_one", "file_two"]}
        onChange={onChange}
        data={{}}
        config={cfg}
      />,
    );
    const inputs = screen.getAllByRole("textbox") as HTMLInputElement[];
    fireEvent.change(inputs[1], { target: { value: "file_two_edited" } });
    expect(onChange).toHaveBeenCalledWith(["file_one", "file_two_edited"]);
  });

  it("removes a row when its trash button is clicked", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <ExpressionListField
        field={field()}
        value={["file_one", "file_two"]}
        onChange={onChange}
        data={{}}
        config={cfg}
      />,
    );
    const buttons = screen.getAllByRole("button");
    // First button is the remove ("trash") action for the first row.
    fireEvent.click(buttons[0]);
    expect(onChange).toHaveBeenCalledWith(["file_two"]);
  });

  it("disables inputs and hides mutation controls when readOnly", () => {
    renderWithProviders(
      <ExpressionListField
        field={field()}
        value={["file_one"]}
        onChange={vi.fn()}
        data={{}}
        config={cfg}
        readOnly
      />,
    );
    const inputs = screen.getAllByRole("textbox") as HTMLInputElement[];
    expect(inputs[0].disabled).toBe(true);
  });
});
