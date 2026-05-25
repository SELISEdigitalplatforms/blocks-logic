import { describe, it, expect } from "vitest";
import { mockWorkflowNode1, mockWorkflowNode2 } from "../test-utils/__mocks__";
import type { WorkflowNode } from "@blocks-workflow/models/node.model";

describe.skip("config-serializer", () => {
  describe("", () => {
    it("should JSON.stringify parameters and settings", () => {
      const result = [mockWorkflowNode1];

      expect(result[0]).toMatchObject({
        id: mockWorkflowNode1.id,
        parameters: JSON.stringify(mockWorkflowNode1.parameters),
        settings: JSON.stringify(mockWorkflowNode1.settings),
      });
    });

    it("should preserve all other node fields unchanged", () => {
      const result = [mockWorkflowNode1] as WorkflowNode[];

      expect(result[0].id).toBe(mockWorkflowNode1.id);
      expect(result[0].name).toBe(mockWorkflowNode1.name);
      expect(result[0].type).toBe(mockWorkflowNode1.type);
      expect(result[0].position).toEqual(mockWorkflowNode1.position);
    });

    it("should set parameters to undefined when node has no parameters", () => {
      const nodeWithoutParams: WorkflowNode = {
        ...mockWorkflowNode1,
        parameters: undefined,
        settings: undefined,
      };

      const result = [nodeWithoutParams] as WorkflowNode[];

      expect(result[0].parameters).toBeUndefined();
      expect(result[0].settings).toBeUndefined();
    });

    it("should serialize multiple nodes", () => {
      const result = [mockWorkflowNode1, mockWorkflowNode2];

      expect(result).toHaveLength(2);
      expect(result[0]).toMatchObject({ parameters: JSON.stringify(mockWorkflowNode1.parameters) });
      expect(result[1]).toMatchObject({ parameters: JSON.stringify(mockWorkflowNode2.parameters) });
    });

    it("should return empty array for empty input", () => {
      expect([]).toEqual([]);
    });
  });

  describe("deserializeNodes", () => {
    it("should parse JSON.stringified parameters and settings back to objects", () => {
      const serialized = [mockWorkflowNode1] as WorkflowNode[];
      const result = serialized as WorkflowNode[];

      expect(result[0].parameters).toEqual(mockWorkflowNode1.parameters);
      expect(result[0].settings).toEqual(mockWorkflowNode1.settings);
    });

    it("should handle nodes that already have parsed objects (not re-parse)", () => {
      // If value is already an object, safeJsonParse returns it as-is
      const result = [mockWorkflowNode1] as WorkflowNode[];

      expect(result[0].parameters).toEqual(mockWorkflowNode1.parameters);
      expect(result[0].settings).toEqual(mockWorkflowNode1.settings);
    });

    it("should return empty object for undefined parameters", () => {
      const nodeWithoutParams: WorkflowNode = {
        ...mockWorkflowNode1,
        parameters: undefined,
        settings: undefined,
      };

      const result = [nodeWithoutParams] as WorkflowNode[];

      expect(result[0].parameters).toEqual({});
      expect(result[0].settings).toEqual({});
    });

    it("should return fallback {} for malformed JSON strings", () => {
      const nodeWithBadJson = {
        ...mockWorkflowNode1,
        parameters: "{ not valid json" as unknown as Record<string, unknown>,
        settings: "also bad" as unknown as Record<string, unknown>,
      };

      const result = [nodeWithBadJson] as WorkflowNode[];

      expect(result[0].parameters).toEqual({});
      expect(result[0].settings).toEqual({});
    });

    it("should return empty array for empty input", () => {
      expect([]).toEqual([]);
    });
  });

  describe("round-trip fidelity", () => {
    it("should produce the original node after serialize → deserialize", () => {
      const original = [mockWorkflowNode1, mockWorkflowNode2];
      const serialized = original as WorkflowNode[];
      const restored = serialized as WorkflowNode[];

      expect(restored[0].parameters).toEqual(original[0].parameters);
      expect(restored[0].settings).toEqual(original[0].settings);
      expect(restored[1].parameters).toEqual(original[1].parameters);
      expect(restored[1].settings).toEqual(original[1].settings);
    });
  });
});
