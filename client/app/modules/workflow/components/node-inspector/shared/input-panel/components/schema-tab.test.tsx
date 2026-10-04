import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { SchemaTab } from "./schema-tab";

const renderSchemaTab = (props: Partial<Parameters<typeof SchemaTab>[0]> = {}) =>
  render(
    <SchemaTab
      runtimeInputRows={[{ id: 1 }, { id: 2 }]}
      isLastExecutionEditor={false}
      nodeName="HTTP Request"
      isDirectParent={true}
      isExecutionMode={false}
      {...props}
    />,
  );

describe("SchemaTab drag references", () => {
  it("renders code references in each mode", () => {
    renderSchemaTab({ target: { kind: "code", mode: "each" } });
    expect(screen.getAllByTitle("Drag to use: $json.id")).toHaveLength(2);
  });

  it("uses the item index in all mode", () => {
    renderSchemaTab({ target: { kind: "code", mode: "all" } });
    expect(screen.getByTitle("Drag to use: $items[0].json.id")).toBeTruthy();
    expect(screen.getByTitle("Drag to use: $items[1].json.id")).toBeTruthy();
  });

  it("keeps expression syntax without a target", () => {
    renderSchemaTab();
    expect(screen.getAllByTitle("Drag to use: {{$json.output.id}}")).toHaveLength(2);
  });
});
