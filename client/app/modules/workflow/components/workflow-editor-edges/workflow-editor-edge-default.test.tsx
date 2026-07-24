import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { EdgeProps } from "@xyflow/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { WorkflowEditorEdgeDefault } from "./workflow-editor-edge-default";
import { NodeExecutionStatus } from "../../utils/workflow-execution-editor.util";

// Keep the real React Flow context (ReactFlowProvider, useReactFlow, getBezierPath)
// but replace the SVG-only primitives with lightweight passthroughs so the edge
// renders under jsdom without a mounted <ReactFlow> canvas.
vi.mock("@xyflow/react", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@xyflow/react")>();
  return {
    ...actual,
    BaseEdge: (props: { id?: string; className?: string; style?: React.CSSProperties }) => (
      <div
        data-testid="base-edge"
        data-edge-id={props.id}
        data-edge-class={props.className ?? ""}
        data-stroke={String(props.style?.stroke ?? "")}
        data-stroke-width={String(props.style?.strokeWidth ?? "")}
      />
    ),
    EdgeLabelRenderer: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  };
});

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const edgeProps = (extra: Record<string, unknown> = {}): EdgeProps =>
  ({
    id: "e1",
    source: "n1",
    target: "n2",
    sourceX: 0,
    sourceY: 0,
    targetX: 100,
    targetY: 100,
    sourcePosition: "bottom",
    targetPosition: "top",
    ...extra,
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
  }) as any;

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const executedNode = (extra: Record<string, unknown> = {}): any => ({
  id: "x1",
  nodeId: "n1",
  nodeName: "n1",
  status: NodeExecutionStatus.Completed,
  outputItemCount: 0,
  outputCountsByBranch: {},
  ...extra,
});

describe("WorkflowEditorEdgeDefault", () => {
  it("renders a plain edge with no label when not traversed", () => {
    renderWithProviders(<WorkflowEditorEdgeDefault {...edgeProps()} />);
    const edge = screen.getByTestId("base-edge");
    expect(edge.getAttribute("data-edge-id")).toBe("e1");
    // no execution -> no execution edge class
    expect(edge.getAttribute("data-edge-class")).toBe("");
    // no branch label and no item-count label rendered
    expect(screen.queryByText("True")).toBeNull();
    expect(screen.queryByText(/item/)).toBeNull();
  });

  it("uses the primary stroke when selected", () => {
    renderWithProviders(
      <WorkflowEditorEdgeDefault {...edgeProps({ selected: true, style: { stroke: "#000" } })} />,
    );
    expect(screen.getByTestId("base-edge").getAttribute("data-stroke")).toBe(
      "hsl(var(--primary))",
    );
  });

  it("shows the branch handle label for if-true", () => {
    renderWithProviders(
      <WorkflowEditorEdgeDefault {...edgeProps({ sourceHandleId: "if-true" })} />,
    );
    expect(screen.getByText("True")).toBeTruthy();
  });

  it("applies execution styling and a singular item count for a traversed completed node", () => {
    renderWithProviders(
      <WorkflowEditorEdgeDefault {...edgeProps({ sourceHandleId: "source" })} />,
      {
        seedWorkflow: (store) => {
          store.setState({
            stepExecutionTraversedEdgeIds: new Set(["e1"]),
            executedNodes: [executedNode({ outputItemCount: 1 })],
          });
        },
      },
    );
    expect(screen.getByText("1 item")).toBeTruthy();
    const edge = screen.getByTestId("base-edge");
    // completed status maps to the success edge class/colour
    expect(edge.getAttribute("data-edge-class")).toBe("stroke-success");
    expect(edge.getAttribute("data-stroke")).toBe("#18c964");
  });

  it("pluralises the item count for multiple items", () => {
    renderWithProviders(
      <WorkflowEditorEdgeDefault {...edgeProps({ sourceHandleId: "source" })} />,
      {
        seedWorkflow: (store) => {
          store.setState({
            stepExecutionTraversedEdgeIds: new Set(["e1"]),
            executedNodes: [executedNode({ outputItemCount: 3 })],
          });
        },
      },
    );
    expect(screen.getByText("3 items")).toBeTruthy();
  });

  it("maps the if-true branch handle to its True branch count", () => {
    renderWithProviders(
      <WorkflowEditorEdgeDefault {...edgeProps({ sourceHandleId: "if-true" })} />,
      {
        seedWorkflow: (store) => {
          store.setState({
            stepExecutionTraversedEdgeIds: new Set(["e1"]),
            executedNodes: [executedNode({ outputCountsByBranch: { True: 2 } })],
          });
        },
      },
    );
    expect(screen.getByText("True")).toBeTruthy();
    expect(screen.getByText("2 items")).toBeTruthy();
  });

  it("falls back to a raw branch handle key present in the branch counts", () => {
    renderWithProviders(
      <WorkflowEditorEdgeDefault {...edgeProps({ sourceHandleId: "custom" })} />,
      {
        seedWorkflow: (store) => {
          store.setState({
            stepExecutionTraversedEdgeIds: new Set(["e1"]),
            executedNodes: [executedNode({ outputCountsByBranch: { custom: 5 } })],
          });
        },
      },
    );
    expect(screen.getByText("5 items")).toBeTruthy();
  });

  it("shows no count label when a traversed node reports zero items", () => {
    renderWithProviders(
      <WorkflowEditorEdgeDefault {...edgeProps({ sourceHandleId: "source" })} />,
      {
        seedWorkflow: (store) => {
          store.setState({
            stepExecutionTraversedEdgeIds: new Set(["e1"]),
            executedNodes: [executedNode({ outputItemCount: 0 })],
          });
        },
      },
    );
    expect(screen.queryByText(/item/)).toBeNull();
    // execution styling still applies for the traversed edge
    expect(screen.getByTestId("base-edge").getAttribute("data-edge-class")).toBe(
      "stroke-success",
    );
  });
});
