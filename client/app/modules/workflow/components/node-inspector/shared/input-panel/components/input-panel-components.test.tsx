import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import type { EditorNode } from "@blocks-workflow/models/node.model";
import { InputPanel } from "../input-panel";
import { DraggableProperty } from "./draggable-property";
import { JsonTab } from "./json-tab";
import { TableTab } from "./table-tab";

const referenceProps = {
  nodeName: "Webhook",
  isDirectParent: true,
  itemIndex: 0,
  itemIsObject: true,
} as const;

const node = (id: string, name: string): EditorNode =>
  ({
    id,
    name,
    type: "webhook",
    category: "trigger",
    version: "v1",
    description: "",
    position: { x: 0, y: 0 },
    parameters: {},
    data: {},
  }) as EditorNode;

describe("input panel components", () => {
  it("sets drag text for draggable properties", () => {
    render(<DraggableProperty segments={["id"]} {...referenceProps} />);
    const dataTransfer = {
      setData: vi.fn(),
      effectAllowed: "",
    };

    fireEvent.dragStart(screen.getByRole("button"), { dataTransfer });

    expect(dataTransfer.setData).toHaveBeenCalledWith("text/plain", "{{$json.output.id}}");
    expect(dataTransfer.effectAllowed).toBe("copy");
  });

  it("renders JSON rows with nested draggable references", () => {
    render(
      <JsonTab
        rows={[{ id: 1, nested: { name: "Ada" } }]}
        nodeName="Webhook"
        isDirectParent
        target={{ kind: "code", mode: "each" }}
      />,
    );

    expect(screen.getByText("item 1:")).toBeTruthy();
    expect(screen.getByTitle("Drag to use: $json.id")).toBeTruthy();
    expect(screen.getByTitle("Drag to use: $json.nested")).toBeTruthy();
    expect(screen.getByTitle("Drag to use: $json.nested.name")).toBeTruthy();
  });

  it("renders table primitive, scalar, and nested object cells", () => {
    render(
      <TableTab
        rows={[42, { id: 7, nested: { name: "Ada" } }]}
        nodeName="Webhook"
        isDirectParent
        target={{ kind: "code", mode: "all" }}
      />,
    );

    expect(screen.getByText("42")).toBeTruthy();
    expect(screen.getByText("7")).toBeTruthy();
    expect(screen.getByTitle("Drag to use: $items[0].json.value")).toBeTruthy();
    expect(screen.getByTitle("Drag to use: $items[1].json.nested.name")).toBeTruthy();
  });

  it("shows empty states", () => {
    const { rerender } = render(
      <JsonTab rows={[]} nodeName="Webhook" isDirectParent target={{ kind: "expression" }} />,
    );
    expect(screen.getByText("No runtime input data available.")).toBeTruthy();

    rerender(
      <TableTab rows={[]} nodeName="Webhook" isDirectParent target={{ kind: "expression" }} />,
    );
    expect(screen.getByText("No runtime input data available.")).toBeTruthy();
  });

  it("renders the selected node input panel from execution data", () => {
    const parent = node("trigger", "Webhook");
    const selected = node("action", "Action");

    renderWithProviders(<InputPanel />, {
      seedWorkflow: (store) => {
        store.setState({
          editorMode: "execution",
          nodesMap: { [parent.id]: parent, [selected.id]: selected },
          edgesMap: {
            edge1: {
              id: "edge1",
              source: parent.id,
              target: selected.id,
            },
          },
          selectedNode: selected,
          executedNodes: [
            {
              id: "run1",
              nodeId: parent.id,
              nodeName: parent.name,
              nodeType: "webhook",
              nodeVersion: "v1",
              runIndex: 0,
              status: 1,
              parameters: {},
              input: [],
              output: [{ id: 1, name: "Ada" }],
              inputItemCount: 0,
              outputItemCount: 1,
              outputCountsByBranch: {},
              startedAt: "2026-09-01T10:00:00Z",
              endedAt: "2026-09-01T10:00:01Z",
              error: null,
              attemptNumber: 1,
            },
          ],
        });
      },
    });

    expect(screen.getByText("Input")).toBeTruthy();
    expect(screen.getByText("id")).toBeTruthy();
    expect(screen.getByText('"Ada"')).toBeTruthy();
  });
});
