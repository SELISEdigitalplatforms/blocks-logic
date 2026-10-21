import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { NodeProps } from "@xyflow/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { NodeDefinitions } from "../node-library-panel/node-definitions";
import { EditorNodeSimple } from "./editor-node-simple";
import { NODE_RENDERERS, VERSIONED_NODE_TYPES, resolveNodeRenderer } from "./node-renderers";
import { WorkflowEditorNodeTypes } from "./workflow-editor-node-types";

describe("NODE_RENDERERS", () => {
  it("H5: registers v1 and v2 for every node type, both rendering EditorNodeSimple", () => {
    expect(Object.keys(NODE_RENDERERS).sort()).toEqual(NodeDefinitions.map((d) => d.type).sort());
    for (const type of Object.keys(NODE_RENDERERS)) {
      expect(NODE_RENDERERS[type].v1).toBe(EditorNodeSimple);
      expect(NODE_RENDERERS[type].v2).toBe(EditorNodeSimple);
    }
  });

  it("H5: falls back to the v1 renderer for an unregistered version", () => {
    expect(resolveNodeRenderer("if", "v9")).toBe(NODE_RENDERERS.if.v1);
    expect(resolveNodeRenderer("if", undefined)).toBe(NODE_RENDERERS.if.v1);
    expect(resolveNodeRenderer("unknown", "v1")).toBeUndefined();
  });

  it("keeps the xyflow node types keyed by node type and referentially stable", () => {
    expect(WorkflowEditorNodeTypes).toBe(VERSIONED_NODE_TYPES);
    expect(Object.keys(WorkflowEditorNodeTypes).sort()).toEqual(NodeDefinitions.map((d) => d.type).sort());
  });

  it("H5: the versioned node renders the renderer registered for the node's version", () => {
    const v2Renderer = vi.fn(() => <span>v2 renderer</span>);
    const original = NODE_RENDERERS.code.v2;
    NODE_RENDERERS.code.v2 = v2Renderer as never;
    try {
      const Versioned = VERSIONED_NODE_TYPES.code;
      renderWithProviders(<Versioned {...({ id: "n1" } as NodeProps)} />, {
        seedWorkflow: (store) => {
          store.getState().setWorkflow({
            itemId: "w1",
            nodes: [{ id: "n1", name: "Code", category: "transform", type: "code", version: "v1", position: { x: 0, y: 0 }, data: {} }],
            edges: [],
          } as never);
        },
      });
      expect(screen.getByText("v2 renderer")).toBeTruthy();
    } finally {
      NODE_RENDERERS.code.v2 = original;
    }
  });
});
