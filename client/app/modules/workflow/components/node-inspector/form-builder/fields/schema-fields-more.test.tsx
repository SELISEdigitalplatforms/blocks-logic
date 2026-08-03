import React from "react";
import { fireEvent, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { SchemaFieldsField } from "./schema-fields-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const cfg: any = { projectKey: "pk", workflowId: "wf", nodeId: "n1" };
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const field = (extra: Record<string, unknown> = {}): any => ({
  id: "f",
  key: "fieldMapping",
  type: "schema-fields",
  ...extra,
});

const schemaFields = [
  { name: "title", type: "string", isArray: false, description: "" },
  {
    name: "author",
    type: "entity",
    isArray: false,
    description: "",
    fields: [{ name: "name", type: "string", isArray: false, description: "" }],
  },
  {
    name: "tags",
    type: "entity",
    isArray: true,
    description: "",
    fields: [{ name: "label", type: "string", isArray: false, description: "" }],
  },
];

afterEach(() => {
  document.body.style.pointerEvents = "";
});

describe("SchemaFieldsField restore/remove", () => {
  it("restores a missing non-array entity via the add popover", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(
      <SchemaFieldsField
        field={field()}
        value={{ title: "T" }}
        onChange={onChange}
        data={{ schemaFields }}
        config={cfg}
        readOnly={false}
      />,
    );
    // Add field popover offers author and tags
    await user.click(screen.getByText(/Add field/));
    await user.click(await screen.findByText("author"));
    // buildRestoreKeys expands entity children (author.name)
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ "author.name": "" }),
    );
  });

  it("restores a missing array entity as an index-0 template", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(
      <SchemaFieldsField
        field={field()}
        value={{ title: "T" }}
        onChange={onChange}
        data={{ schemaFields }}
        config={cfg}
        readOnly={false}
      />,
    );
    await user.click(screen.getByText(/Add field/));
    await user.click(await screen.findByText("tags"));
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ "tags.0.label": "" }),
    );
  });

  it("removes a non-array entity and its keys", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <SchemaFieldsField
        field={field()}
        value={{ title: "T", "author.name": "Ada" }}
        onChange={onChange}
        data={{ schemaFields: [schemaFields[1]] }}
        config={cfg}
        readOnly={false}
      />,
    );
    // the entity header trash button removes the whole author prefix
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[0]);
    const arg = onChange.mock.calls[0][0] as Record<string, unknown>;
    expect("author.name" in arg).toBe(false);
  });

  it("removes a scalar field via its row trash", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <SchemaFieldsField
        field={field()}
        value={{ title: "T" }}
        onChange={onChange}
        data={{ schemaFields: [schemaFields[0]] }}
        config={cfg}
        readOnly={false}
      />,
    );
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[buttons.length - 1]);
    expect(onChange).toHaveBeenCalledWith({});
  });
});
