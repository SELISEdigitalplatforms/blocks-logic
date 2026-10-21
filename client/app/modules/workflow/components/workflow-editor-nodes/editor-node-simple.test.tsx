import { act, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import type { WorkflowStore } from "../../store";
import { NodeDefinitions } from "../node-library-panel/node-definitions";
import { EditorNodeSimple, handleColumnMinHeight } from "./editor-node-simple";

// Handle ids each type had on dev before SPEC5; saved edges reference these.
const OLD_HANDLE_IDS: Record<string, { source: string[]; target: string[] }> = {
  webhook: { source: ["source"], target: [] },
  email: { source: ["source"], target: [] },
  dataGateway: { source: ["source"], target: [] },
  schedule: { source: ["source"], target: [] },
  agent: { source: ["source"], target: ["target"] },
  sendMail: { source: ["source"], target: ["target"] },
  httpRequest: { source: ["source"], target: ["target"] },
  proxy: { source: ["source"], target: ["target"] },
  dataAction: { source: ["source"], target: ["target"] },
  function: { source: ["source"], target: ["target"] },
  if: { source: ["if-true", "if-false"], target: ["target"] },
  setfield: { source: ["source"], target: ["target"] },
  code: { source: ["source"], target: ["target"] },
};

const updateNodeInternals = vi.hoisted(() => vi.fn());

// xyflow's <Handle> needs a mounted node; render it as a marker carrying its id and side instead.
vi.mock("@xyflow/react", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@xyflow/react")>();
  return {
    ...actual,
    useUpdateNodeInternals: () => updateNodeInternals,
    Handle: ({ id, type, children }: { id: string; type: string; children?: React.ReactNode }) => (
      <div data-testid={`handle-${type}`} data-handle-id={id}>
        {children}
      </div>
    ),
  };
});

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const storedNode = (extra: Record<string, unknown> = {}): any => ({
  id: "n1",
  name: "Node",
  category: "logic",
  type: "if",
  version: "v1",
  position: { x: 0, y: 0 },
  parameters: {},
  data: { hasHandleArrow: true },
  ...extra,
});

const load =
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (nodes: any[], edges: any[] = [], mode: "editor" | "execution" | "version" = "editor") =>
    (store: WorkflowStore) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      store.getState().setWorkflow({ itemId: "w1", nodes, edges } as any);
      store.getState().setEditorMode(mode);
    };

const handleIds = (type: "source" | "target") =>
  screen.queryAllByTestId(`handle-${type}`).map((el) => el.getAttribute("data-handle-id"));

beforeEach(() => updateNodeInternals.mockClear());

describe("EditorNodeSimple", () => {
  it.each(NodeDefinitions.map((d) => [`${d.category}/${d.type}`, d] as const))(
    "H3: renders a stored v1 %s node with the same handles as before",
    (_name, definition) => {
      renderWithProviders(<EditorNodeSimple {...({ id: "n1" } as never)} />, {
        seedWorkflow: load([storedNode({ category: definition.category, type: definition.type })]),
      });
      expect(handleIds("target")).toEqual(OLD_HANDLE_IDS[definition.type].target);
      expect(handleIds("source")).toEqual(OLD_HANDLE_IDS[definition.type].source);
      expect(screen.getByText(definition.title)).toBeTruthy();
    },
  );

  it("H3: shows True/False on the If outputs and no label on other nodes", () => {
    const { unmount } = renderWithProviders(<EditorNodeSimple {...({ id: "n1" } as never)} />, {
      seedWorkflow: load([storedNode()]),
    });
    expect(screen.getByText("True")).toBeTruthy();
    expect(screen.getByText("False")).toBeTruthy();
    unmount();

    renderWithProviders(<EditorNodeSimple {...({ id: "n1" } as never)} />, {
      seedWorkflow: load([storedNode({ category: "transform", type: "code" })]),
    });
    expect(screen.queryByText("True")).toBeNull();
  });

  it("takes handle labels from the node's own entries", () => {
    renderWithProviders(<EditorNodeSimple {...({ id: "n1" } as never)} />, {
      seedWorkflow: load([
        storedNode({
          version: "v2",
          handle: {
            sources: [
              { key: "if-true", label: "Yes" },
              { key: "if-false", label: "No" },
            ],
            targets: [{ key: "target", label: "" }],
          },
        }),
      ]),
    });
    expect(screen.getByText("Yes")).toBeTruthy();
    expect(screen.getByText("No")).toBeTruthy();
  });

  it("C4/C10: in read-only modes shows only connected handles; an edge without handles matches source/target", () => {
    renderWithProviders(<EditorNodeSimple {...({ id: "n1" } as never)} />, {
      seedWorkflow: load(
        [storedNode({ category: "transform", type: "code" }), storedNode({ id: "n0" }), storedNode({ id: "n2" })],
        [
          { id: "in", source: "n0", target: "n1", sourceHandle: "if-true" },
          { id: "out", source: "n1", target: "n2", targetHandle: "target" },
        ],
        "execution",
      ),
    });
    expect(handleIds("target")).toEqual(["target"]);
    expect(handleIds("source")).toEqual(["source"]);
  });

  it("hides an unconnected If output in read-only mode", () => {
    renderWithProviders(<EditorNodeSimple {...({ id: "n1" } as never)} />, {
      seedWorkflow: load(
        [storedNode(), storedNode({ id: "n2", category: "transform", type: "code" })],
        [{ id: "e", source: "n1", target: "n2", sourceHandle: "if-false", targetHandle: "target" }],
        "version",
      ),
    });
    expect(handleIds("source")).toEqual(["if-false"]);
  });

  it("C1: renders a node whose stored version is unknown as v1", () => {
    renderWithProviders(<EditorNodeSimple {...({ id: "n1" } as never)} />, {
      seedWorkflow: load([storedNode({ version: "1.0" })]),
    });
    expect(handleIds("source")).toEqual(["if-true", "if-false"]);
  });

  it("H6: re-measures the node when its handle keys change, not on first render", () => {
    let store: WorkflowStore | undefined;
    renderWithProviders(<EditorNodeSimple {...({ id: "n1" } as never)} />, {
      seedWorkflow: (s) => {
        store = s;
        load([storedNode()])(s);
      },
    });
    expect(updateNodeInternals).not.toHaveBeenCalled();

    act(() => {
      store!.getState().updateNode("n1", { name: "Renamed" });
    });
    expect(updateNodeInternals).not.toHaveBeenCalled();

    act(() => {
      store!.getState().updateNode("n1", {
        handle: {
          sources: [
            { key: "if-true", label: "True" },
            { key: "if-false", label: "False" },
            { key: "extra", label: "" },
          ],
          targets: [{ key: "target", label: "" }],
        },
      });
    });
    expect(updateNodeInternals).toHaveBeenCalledTimes(1);
    expect(updateNodeInternals).toHaveBeenCalledWith("n1");
    expect(handleIds("source")).toEqual(["if-true", "if-false", "extra"]);
  });

  describe("H14: card min-height", () => {
    it.each([
      [1, undefined],
      [2, undefined],
      [5, 114],
      [12, 296],
    ])("for %i handles per side is %s", (count, expected) => {
      expect(handleColumnMinHeight(count)).toBe(expected);
    });

    it("leaves a two-handle node's title unwrapped and grows a node with five outputs", () => {
      const sources = ["a", "b", "c", "d", "e"].map((key) => ({ key, label: "" }));
      const { container, unmount } = renderWithProviders(<EditorNodeSimple {...({ id: "n1" } as never)} />, {
        seedWorkflow: load([storedNode()]),
      });
      expect(container.querySelector("[style*='min-height']")).toBeNull();
      unmount();

      const grown = renderWithProviders(<EditorNodeSimple {...({ id: "n1" } as never)} />, {
        seedWorkflow: load([
          storedNode({ version: "v2", handle: { sources, targets: [{ key: "target", label: "" }] } }),
        ]),
      });
      const wrapper = grown.container.querySelector<HTMLElement>("[style*='min-height']");
      expect(wrapper?.style.minHeight).toBe("114px");
    });
  });
});
