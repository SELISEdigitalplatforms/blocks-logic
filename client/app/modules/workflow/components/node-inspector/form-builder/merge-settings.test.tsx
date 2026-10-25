import { fireEvent, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { FormBuilder } from "./form-builder";
import { NodeSchemasDefinition } from "../../node-schemas/node-schemas";

describe("Merge settings through the form builder", () => {
  const schema = NodeSchemasDefinition.transformmergev2.schema;
  const saved = { mode: "append", numberInputs: 2, runWhen: "all" };

  it("saves Number of Inputs as a number only when the field is left, kept within 2 to 10", () => {
    const onChange = vi.fn();
    renderWithProviders(<FormBuilder fields={schema.parameters} data={saved} onChange={onChange} />);

    const numberInputs = screen.getByRole("spinbutton") as HTMLInputElement;
    expect(numberInputs.value).toBe("2");
    fireEvent.change(numberInputs, { target: { value: "1" } });
    fireEvent.change(numberInputs, { target: { value: "12" } });
    expect(onChange).not.toHaveBeenCalled();
    fireEvent.blur(numberInputs);
    expect(onChange).toHaveBeenLastCalledWith({ ...saved, numberInputs: 10 });
  });

  it("shows Run When and saves the chosen option", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(<FormBuilder fields={schema.parameters} data={saved} onChange={onChange} />);

    const [, runWhen] = screen.getAllByRole("combobox");
    expect(runWhen.textContent).toBe("All");
    await user.click(runWhen);
    expect(await screen.findByText("Output the data from every input that has some. Empty inputs are skipped.")).toBeTruthy();
    await user.click(screen.getByRole("option", { name: /^Any/ }));

    expect(onChange).toHaveBeenLastCalledWith({ ...saved, runWhen: "any" });
  });
});
