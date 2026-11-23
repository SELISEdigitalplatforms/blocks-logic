import { describe, expect, it } from "vitest";
import fc from "fast-check";
import { buildWorkflowExport } from "./workflow-export";
import { preflightWorkflowFile, remapAndSanitiseWorkflow } from "./workflow-import";
import { Workflow } from "../models/workflow.model";

// I04-WP5 (TypeScript part): for any generated workflow graph, export then import returns an equal graph, modulo the
// id remapping the import does on purpose. A failing property has found a bug; keep its seed and report it.

const hexId = fc.stringMatching(/^[a-f0-9]{32}$/);

const nodeArb = fc.record({
  id: hexId,
  name: fc.string({ minLength: 1, maxLength: 20 }),
  category: fc.constantFrom("trigger", "action", "logic"),
  type: fc.constantFrom("webhook", "http", "set"),
  version: fc.constant("v1"),
  position: fc.record({
    x: fc.integer({ min: -500, max: 500 }),
    y: fc.integer({ min: -500, max: 500 }),
  }),
  parameters: fc.dictionary(
    fc.stringMatching(/^[a-z][a-z0-9]{0,7}$/),
    fc.string({ maxLength: 12 }),
    { maxKeys: 3 },
  ),
});

const graphArb = fc
  .uniqueArray(nodeArb, { minLength: 1, maxLength: 5, selector: (n) => n.id })
  .chain((nodes) =>
    fc
      .array(fc.tuple(fc.nat(), fc.nat()), { maxLength: 6 })
      .map((pairs) => {
        const edges = pairs
          .map(([a, b], i) => ({
            id: `e${i}`,
            source: nodes[a % nodes.length].id,
            target: nodes[b % nodes.length].id,
            sourceHandle: "out",
            targetHandle: "in",
          }))
          .filter((e) => e.source !== e.target);
        return { nodes, edges };
      }),
  );

// The part of a node that survives the round trip unchanged: everything except its id.
const nodeSignature = (n: Record<string, unknown>): string =>
  JSON.stringify([
    n.name,
    n.category,
    n.type,
    n.version,
    n.position,
    Object.keys((n.parameters as Record<string, unknown>) ?? {}).sort(),
  ]);

describe("workflow export then import (property)", () => {
  it("returns an equal graph, modulo id remapping, for any generated graph", () => {
    fc.assert(
      fc.property(graphArb, (graph) => {
        const workflow = {
          name: "wf",
          settings: {},
          nodes: graph.nodes,
          edges: graph.edges,
        } as unknown as Workflow;

        const text = JSON.stringify(buildWorkflowExport(workflow));
        const preflight = preflightWorkflowFile({ text, size: text.length });
        expect(preflight.ok).toBe(true);
        if (!preflight.ok) return;

        const imported = remapAndSanitiseWorkflow(preflight.root);
        expect(imported.issues).toBe(0);
        expect(imported.nodes.length).toBe(graph.nodes.length);
        expect(imported.edges.length).toBe(graph.edges.length);

        const before = graph.nodes.map((n) => nodeSignature(n as unknown as Record<string, unknown>)).sort();
        const after = imported.nodes.map((n) => nodeSignature(n)).sort();
        expect(after).toEqual(before);

        const importedIds = new Set(imported.nodes.map((n) => n.id));
        for (const edge of imported.edges) {
          expect(importedIds.has(edge.source)).toBe(true);
          expect(importedIds.has(edge.target)).toBe(true);
        }
      }),
      { numRuns: 200 },
    );
  });
});
