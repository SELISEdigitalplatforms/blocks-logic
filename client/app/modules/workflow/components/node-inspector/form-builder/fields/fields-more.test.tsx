import React from "react";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { RadioField } from "./radio-field";
import { CheckboxField } from "./checkbox-field";
import { CalloutAccordionDisplayField } from "./callout-accordion-display-field";
import { ConditionsField } from "./conditions-field";
import { SchemaFieldPickerField } from "./schema-field-picker-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const cfg: any = { projectKey: "pk", workflowId: "wf", nodeId: "n1" };
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const field = (extra: Record<string, unknown> = {}): any => ({
  id: "f",
  key: "f",
  type: "text",
  ...extra,
});

describe("RadioField", () => {
  it("renders static options and reports a selection", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <RadioField
        field={field({
          id: "r",
          type: "radio",
          options: [
            { value: "a", label: "Alpha" },
            { value: "b", label: "Beta" },
          ],
        })}
        value="a"
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    expect(screen.getByText("Alpha")).toBeTruthy();
    fireEvent.click(screen.getByText("Beta"));
    expect(onChange).toHaveBeenCalledWith("b");
  });

  it("loads async options", async () => {
    const options = vi
      .fn()
      .mockResolvedValue([{ value: "x", label: "Xray" }]);
    renderWithProviders(
      <RadioField
        field={field({ id: "r2", type: "radio", options })}
        value=""
        onChange={vi.fn()}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    await waitFor(() => expect(options).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByText("Xray")).toBeTruthy());
  });
});

describe("CheckboxField", () => {
  it("toggles the checked state", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <CheckboxField
        field={field({ id: "c", type: "checkbox", placeholder: "Accept" })}
        value={false}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    fireEvent.click(screen.getByRole("checkbox"));
    expect(onChange).toHaveBeenCalledWith(true);
  });
});

describe("CalloutAccordionDisplayField", () => {
  it("renders string title and description as markdown", () => {
    const { container } = renderWithProviders(
      <CalloutAccordionDisplayField
        field={field({ id: "ca", type: "callout-accordion-display" })}
        value={{ title: "Heads up", description: "Some **note**" }}
        onChange={vi.fn()}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    expect(container.textContent).toContain("Heads up");
  });

  it("renders React-node content directly", () => {
    renderWithProviders(
      <CalloutAccordionDisplayField
        field={field({ id: "ca2", type: "callout-accordion-display" })}
        value={{ title: <span>Node Title</span>, description: <span>Body</span> }}
        onChange={vi.fn()}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    expect(screen.getByText("Node Title")).toBeTruthy();
  });

  it("returns null for a non-object value", () => {
    const { container } = renderWithProviders(
      <CalloutAccordionDisplayField
        field={field({ id: "ca3", type: "callout-accordion-display" })}
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        value={"not-object" as any}
        onChange={vi.fn()}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    expect(container.textContent).toBe("");
  });
});

describe("ConditionsField interactions", () => {
  const render1 = (onChange: (v: unknown) => void) =>
    renderWithProviders(
      <ConditionsField
        field={field({ id: "cond", type: "conditions" })}
        value={[{ left: "a", operator: "equals", right: "b", type: "string" }]}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );

  it("edits the right operand", () => {
    const onChange = vi.fn();
    render1(onChange);
    const right = document.getElementById("cond-right-0") as HTMLInputElement;
    fireEvent.change(right, { target: { value: "z" } });
    expect(onChange).toHaveBeenCalledWith([
      expect.objectContaining({ right: "z" }),
    ]);
  });

  it("changes the operator and type via the selects", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render1(onChange);
    // two selects: type and operator
    const triggers = screen.getAllByRole("combobox");
    await user.click(triggers[0]);
    await user.click(await screen.findByText("Boolean"));
    // boolean resets operator; the right operand disappears for unary operators
    expect(onChange).toHaveBeenCalled();
  });
});

describe("SchemaFieldPickerField add flow", () => {
  const schemaFields = [
    { name: "title", type: "string", isArray: false, description: "" },
    {
      name: "author",
      type: "entity",
      isArray: false,
      description: "",
      fields: [{ name: "name", type: "string", isArray: false, description: "" }],
    },
  ];

  it("re-adds a removed field through the popover", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(
      <SchemaFieldPickerField
        field={field({ id: "sfp", type: "schema-field-picker" })}
        // only author included, title removed -> Add Field popover offers title
        value={["author", "author.name"]}
        onChange={onChange}
        data={{ schemaFields }}
        config={cfg}
        readOnly={false}
      />,
    );
    await user.click(screen.getByText("Add Field"));
    await user.click(await screen.findByText("title"));
    expect(onChange).toHaveBeenCalledWith(
      expect.arrayContaining(["title"]),
    );
  });
});
