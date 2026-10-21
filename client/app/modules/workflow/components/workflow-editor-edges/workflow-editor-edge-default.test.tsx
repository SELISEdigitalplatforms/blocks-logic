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

// The source node "n1" as an If node: edge labels come from its handle entries.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const ifSourceNode = (): any => ({
  n1: {
    id: "n1",
    name: "If",
    type: "if",
    category: "logic",
    version: "v2",
    position: { x: 0, y: 0 },
    data: {},
    handle: {
      sources: [
        { key: "if-true", label: "True" },
        { key: "if-false", label: "False" },
      ],
      targets: [{ key: "target", label: "" }],
    },
  },
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
      { seedWorkflow: (store) => store.setState({ nodesMap: ifSourceNode() }) },
    );
    expect(screen.getByText("True")).toBeTruthy();
  });

  it("takes the label from the source node's handle entry", () => {
    const nodesMap = ifSourceNode();
    nodesMap.n1.handle.sources[1].label = "Otherwise";
    renderWithProviders(
      <WorkflowEditorEdgeDefault {...edgeProps({ sourceHandleId: "if-false" })} />,
      { seedWorkflow: (store) => store.setState({ nodesMap }) },
    );
    expect(screen.getByText("Otherwise")).toBeTruthy();
  });

  it("shows no label when the source node has no entry for the handle", () => {
    renderWithProviders(
      <WorkflowEditorEdgeDefault {...edgeProps({ sourceHandleId: "if-true" })} />,
    );
    expect(screen.queryByText("True")).toBeNull();
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
            nodesMap: ifSourceNode(),
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

describe("WorkflowEditorEdgeDefault on a Switch (SPEC6)", () => {
  const A = "switch-handle-source-aaaaa1";
  const B = "switch-handle-source-bbbbb2";
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const switchSource = (): any => ({
    n1: {
      id: "n1", name: "Switch", category: "logic", type: "switch", version: "v2", position: { x: 0, y: 0 }, data: {},
      handle: {
        targets: [{ key: "target", label: "" }],
        sources: [
          { key: A, label: "Paid" },
          { key: B, label: "" },
          { key: "switch-handle-source-fallback", label: "" },
        ],
      },
    },
  });

  it.each([
    [A, "Paid"],
    [B, "Output 1"],
    ["switch-handle-source-fallback", "Fallback"],
  ])("labels the edge on %s as %s", (sourceHandleId, label) => {
    renderWithProviders(<WorkflowEditorEdgeDefault {...edgeProps({ sourceHandleId })} />, {
      seedWorkflow: (store) => store.setState({ nodesMap: switchSource() }),
    });
    expect(screen.getByText(label)).toBeTruthy();
  });

  it("H13: shows the item count of the edge's own key", () => {
    renderWithProviders(<WorkflowEditorEdgeDefault {...edgeProps({ sourceHandleId: B })} />, {
      seedWorkflow: (store) =>
        store.setState({
          nodesMap: switchSource(),
          stepExecutionTraversedEdgeIds: new Set(["e1"]),
          executedNodes: [executedNode({ outputCountsByBranch: { [A]: 1, [B]: 3 } })],
        }),
    });
    expect(screen.getByText("3 items")).toBeTruthy();
  });
});
