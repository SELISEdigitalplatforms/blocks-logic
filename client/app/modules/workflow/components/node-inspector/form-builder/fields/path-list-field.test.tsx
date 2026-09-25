import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { FieldSchema } from "../form-field.types";
import { PathListField } from "./path-list-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const cfg: any = { tenantId: "pk", workflowId: "wf", nodeId: "n1" };
const field: FieldSchema = { id: "fields", key: "fields", type: "path-list" };

describe("PathListField", () => {
  it("lists the paths sorted by name, nested ones under their parent, with a count", () => {
    renderWithProviders(
      <PathListField
        field={field}
        value={["user.name", "id", "items[].sku", "user.address.city", "Amount"]}
        onChange={() => {}}
        data={{}}
        config={cfg}
      />,
    );

    const rows = screen.getAllByRole("listitem").map((row) => row.textContent);
    expect(rows).toEqual(["Amount", "id", "items[].sku", "user.address.city", "user.name"]);
    expect(screen.getByText("5")).toBeTruthy();
  });
});
