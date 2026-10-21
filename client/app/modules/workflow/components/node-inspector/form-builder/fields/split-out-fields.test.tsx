import { fireEvent, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { FormBuilder } from "../form-builder";
import { NodeSchemasDefinition } from "../../../node-schemas/node-schemas";
import { SplitOutFieldsField } from "./split-out-fields-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const config: any = { tenantId: "t", workflowId: "w", nodeId: "n" };
const fieldBox = (n: number) => screen.getByLabelText(`Field to split out ${n}`) as HTMLInputElement;
const destBox = (n: number) => screen.getByLabelText(`Destination field name ${n}`) as HTMLInputElement;

describe("SplitOutFieldsField", () => {
  const renderField = (value = [{ field: "lines", destination: "" }]) => {
    const onChange = vi.fn();
    renderWithProviders(
      <SplitOutFieldsField field={{ id: "f", key: "splitOutFields", type: "split-out-fields" }} value={value} onChange={onChange} data={{}} config={config} />,
    );
    return onChange;
  };

  it("shows n8n's placeholders", () => {
    renderField([{ field: "", destination: "" }]);
    expect(fieldBox(1).placeholder).toBe("Drag a field from the left or type its name");
    expect(destBox(1).placeholder).toBe("Destination field name");
  });

  it("adds and removes rows; the last row cannot be removed", () => {
    const onChange = renderField();
    expect((screen.getByRole("button", { name: "Remove field 1" }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: /Add field/ }));
    expect(onChange).toHaveBeenLastCalledWith([{ field: "lines", destination: "" }, { field: "", destination: "" }]);
    fireEvent.change(fieldBox(2), { target: { value: "tags" } });
    expect(screen.getByText(/combined by position/)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Remove field 1" }));
    expect(onChange).toHaveBeenLastCalledWith([{ field: "tags", destination: "" }]);
  });

  it("a field dragged from the input panel is dropped as its path", () => {
    const onChange = renderField([{ field: "", destination: "" }]);
    fireEvent.drop(fieldBox(1), { dataTransfer: { getData: () => "{{$json.output.order.lines}}" } });
    expect(onChange).toHaveBeenLastCalledWith([{ field: "order.lines", destination: "" }]);
  });

  it("a typed $json path is tidied when the box loses focus", () => {
    const onChange = renderField([{ field: "", destination: "" }]);
    fireEvent.change(fieldBox(1), { target: { value: "$json.lines" } });
    fireEvent.blur(fieldBox(1));
    expect(onChange).toHaveBeenLastCalledWith([{ field: "lines", destination: "" }]);
  });

  it("read-only disables every control", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <SplitOutFieldsField field={{ id: "f", key: "splitOutFields", type: "split-out-fields", disabled: true }} value={[{ field: "lines", destination: "" }]} onChange={onChange} data={{}} readOnly config={config} />,
    );
    expect(fieldBox(1).disabled).toBe(true);
    expect(destBox(1).disabled).toBe(true);
    expect((screen.getByRole("button", { name: /Add field/ }) as HTMLButtonElement).disabled).toBe(true);
  });
});

describe("Split Out settings through the form builder", () => {
  it("saves the same strings as before, never the rows", () => {
    const schema = NodeSchemasDefinition.transformsplitOutv2.schema;
    const onChange = vi.fn();
    renderWithProviders(
      <FormBuilder fields={schema.parameters} data={{ fieldToSplitOut: "lines", include: "noOtherFields", fieldsToInclude: "", destinationFieldName: "", disableDotNotation: false }} onChange={onChange} />,
    );
    expect(fieldBox(1).value).toBe("lines");
    fireEvent.change(destBox(1), { target: { value: "line" } });
    const saved = onChange.mock.calls.at(-1)![0] as Record<string, unknown>;
    expect(saved).toMatchObject({ fieldToSplitOut: "lines", destinationFieldName: "line", include: "noOtherFields" });
    expect(saved).not.toHaveProperty("splitOutFields");
  });
});
