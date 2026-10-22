import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { FormBuilder } from "./form-builder";
import { NodeSchemasDefinition } from "../../node-schemas/node-schemas";

describe("Merge settings through the form builder", () => {
  const schema = NodeSchemasDefinition.transformmergev2.schema;

  it("shows the saved Number of Inputs and saves the chosen one as a number", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(<FormBuilder fields={schema.parameters} data={{ mode: "append", numberInputs: 2 }} onChange={onChange} />);

    const [, numberInputs] = screen.getAllByRole("combobox");
    expect(numberInputs.textContent).toBe("2");
    await user.click(numberInputs);
    await user.click(await screen.findByRole("option", { name: "4" }));

    expect(onChange).toHaveBeenLastCalledWith({ mode: "append", numberInputs: 4 });
  });
});
