import { fireEvent, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { FormBuilder } from "../form-builder";
import { NodeSchemasDefinition } from "../../../node-schemas/node-schemas";
import { SplitOutFieldsField } from "./split-out-fields-field";
import { CommaListField } from "./comma-list-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const config: any = { tenantId: "t", workflowId: "w", nodeId: "n" };
const fieldBox = (n: number) => screen.getByLabelText(`Field to split out ${n}`) as HTMLInputElement;
const outputBox = (n: number) => screen.getByLabelText(`Output field name ${n}`) as HTMLInputElement;
const row = (fieldName: string, outputFieldName = "") => ({ fieldName, outputFieldName });

describe("SplitOutFieldsField", () => {
  const renderField = (value: unknown = [row("lines")]) => {
    const onChange = vi.fn();
    renderWithProviders(
      <SplitOutFieldsField field={{ id: "f", key: "fields", type: "split-out-fields" }} value={value as never} onChange={onChange} data={{}} config={config} />,
    );
    return onChange;
  };

  it("shows the saved fields, or one empty row", () => {
    renderField([row("names", "name"), row("scores")]);
    expect(fieldBox(1).value).toBe("names");
    expect(outputBox(1).value).toBe("name");
    expect(fieldBox(2).value).toBe("scores");
  });

  it("starts with one empty row when nothing is saved", () => {
    renderField(null);
    expect(fieldBox(1).value).toBe("");
    expect(fieldBox(1).placeholder).toBe("Drag a field from the left or type its name");
    expect(outputBox(1).placeholder).toBe("Output field name");
  });

  it("saves each row as { fieldName, outputFieldName }; empty rows are not saved", () => {
    const onChange = renderField();
    fireEvent.click(screen.getByRole("button", { name: /Add field/ }));
    expect(onChange).toHaveBeenLastCalledWith([row("lines")]);
    fireEvent.change(fieldBox(2), { target: { value: "scores" } });
    fireEvent.change(outputBox(2), { target: { value: "score" } });
    expect(onChange).toHaveBeenLastCalledWith([row("lines"), row("scores", "score")]);
    expect(screen.getByText(/combined by position/)).toBeTruthy();
  });

  it("removes a row; the last row cannot be removed", () => {
    const onChange = renderField([row("lines"), row("tags")]);
    fireEvent.click(screen.getByRole("button", { name: "Remove field 1" }));
    expect(onChange).toHaveBeenLastCalledWith([row("tags")]);
    expect((screen.getByRole("button", { name: "Remove field 1" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("a field dragged from the input panel is dropped as its path", () => {
    const onChange = renderField([row("")]);
    fireEvent.drop(fieldBox(1), { dataTransfer: { getData: () => "{{$json.output.order.lines}}" } });
    expect(onChange).toHaveBeenLastCalledWith([row("order.lines")]);
  });

  it("a typed $json path is tidied when the box loses focus", () => {
    const onChange = renderField([row("")]);
    fireEvent.change(fieldBox(1), { target: { value: "$json.lines" } });
    fireEvent.blur(fieldBox(1));
    expect(onChange).toHaveBeenLastCalledWith([row("lines")]);
  });

  it("read-only disables every control", () => {
    renderWithProviders(
      <SplitOutFieldsField field={{ id: "f", key: "fields", type: "split-out-fields", disabled: true }} value={[row("lines")]} onChange={vi.fn()} data={{}} readOnly config={config} />,
    );
    expect(fieldBox(1).disabled).toBe(true);
    expect(outputBox(1).disabled).toBe(true);
    expect((screen.getByRole("button", { name: /Add field/ }) as HTMLButtonElement).disabled).toBe(true);
  });
});

describe("CommaListField", () => {
  it("saves a list but keeps the text as typed", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <CommaListField field={{ id: "c", key: "fieldsToInclude", type: "comma-list", placeholder: "e.g. email, name" }} value={["email"]} onChange={onChange} data={{}} config={config} />,
    );
    const box = screen.getByPlaceholderText("e.g. email, name") as HTMLInputElement;
    expect(box.value).toBe("email");
    fireEvent.change(box, { target: { value: "email, " } });
    expect(box.value).toBe("email, ");
    expect(onChange).toHaveBeenLastCalledWith(["email"]);
    fireEvent.change(box, { target: { value: "email, name" } });
    expect(onChange).toHaveBeenLastCalledWith(["email", "name"]);
  });
});

describe("Split Out settings through the form builder", () => {
  const schema = NodeSchemasDefinition.transformsplitOutv2.schema;
  const saved = { fields: [row("lines")], include: "selectedOtherFields", fieldsToInclude: ["orderId"], disableDotNotation: false };

  it("saves fields and fieldsToInclude as lists, and nothing else", () => {
    const onChange = vi.fn();
    renderWithProviders(<FormBuilder fields={schema.parameters} data={saved} onChange={onChange} />);
    expect(fieldBox(1).value).toBe("lines");
    fireEvent.change(outputBox(1), { target: { value: "line" } });
    expect(onChange.mock.calls.at(-1)![0]).toEqual({ ...saved, fields: [row("lines", "line")] });

    fireEvent.change(screen.getByPlaceholderText("e.g. email, name"), { target: { value: "orderId, customer" } });
    expect(onChange.mock.calls.at(-1)![0]).toEqual({ ...saved, fields: [row("lines", "line")], fieldsToInclude: ["orderId", "customer"] });
  });
});
