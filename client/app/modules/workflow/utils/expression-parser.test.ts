import { describe, it, expect } from "vitest";
import {
  parseExpressionToBackend,
  parseExpressionFromBackend,
  extractNodeReferences,
  validateExpression,
  getNodeBackendRef,
  getNodeAlias,
} from "./expression-parser";
import type { EditorNode } from "@blocks-workflow/models/node.model";

// ─── Test Fixtures ────────────────────────────────────────────────────────────
const mockTriggerNode: EditorNode = {
  id: "node-trigger-001",
  name: "webhook",
  description: "Webhook trigger",
  type: "webhook",
  category: "trigger",
  version: "v1",
  position: { x: 100, y: 100 },
  data: {},
};

const mockActionNode: EditorNode = {
  id: "node-action-001",
  name: "sendEmail",
  description: "Send email action",
  type: "sendMail",
  category: "action",
  version: "v1",
  position: { x: 400, y: 100 },
  data: {},
};

const nodesMap: Record<string, EditorNode> = {
  [mockTriggerNode.id]: mockTriggerNode,
  [mockActionNode.id]: mockActionNode,
};

describe("parseExpressionToBackend", () => {
  it("should convert {{nodes.name.field}} to backend node_id_handle.field format", () => {
    const result = parseExpressionToBackend("{{nodes.webhook.data.name}}", nodesMap);
    expect(result).toBe("node_node-trigger-001_trigger.data.name");
  });

  it("should use action handle for action-category nodes", () => {
    const result = parseExpressionToBackend("{{nodes.sendEmail.status}}", nodesMap);
    expect(result).toBe("node_node-action-001_action.status");
  });

  it("should convert node reference without a field path", () => {
    const result = parseExpressionToBackend("{{nodes.webhook}}", nodesMap);
    expect(result).toBe("node_node-trigger-001_trigger");
  });

  it("should replace multiple references in a single expression", () => {
    const expr = "{{nodes.webhook.id}} and {{nodes.sendEmail.to}}";
    const result = parseExpressionToBackend(expr, nodesMap);
    expect(result).toBe("node_node-trigger-001_trigger.id and node_node-action-001_action.to");
  });

  it("should leave unknown node references unchanged", () => {
    const result = parseExpressionToBackend("{{nodes.unknownNode.field}}", nodesMap);
    expect(result).toBe("{{nodes.unknownNode.field}}");
  });

  it("should return empty string for empty input", () => {
    expect(parseExpressionToBackend("", nodesMap)).toBe("");
  });

  it("should return non-expression strings unchanged", () => {
    expect(parseExpressionToBackend("plain text", nodesMap)).toBe("plain text");
  });
});

describe("parseExpressionFromBackend", () => {
  it("should convert backend node_id_handle.field to {{nodes.name.field}} format", () => {
    const result = parseExpressionFromBackend("node_node-trigger-001_trigger.data.name", nodesMap);
    expect(result).toBe("{{nodes.webhook.data.name}}");
  });

  it("should convert backend reference without a field path", () => {
    const result = parseExpressionFromBackend("node_node-trigger-001_trigger", nodesMap);
    expect(result).toBe("{{nodes.webhook}}");
  });

  it("should replace multiple backend references", () => {
    const expr = "node_node-trigger-001_trigger.id and node_node-action-001_action.to";
    const result = parseExpressionFromBackend(expr, nodesMap);
    expect(result).toBe("{{nodes.webhook.id}} and {{nodes.sendEmail.to}}");
  });

  it("should leave unknown node IDs unchanged", () => {
    const result = parseExpressionFromBackend("node_unknown-id_trigger.field", nodesMap);
    expect(result).toBe("node_unknown-id_trigger.field");
  });

  it("should return empty string for empty input", () => {
    expect(parseExpressionFromBackend("", nodesMap)).toBe("");
  });
});

describe("parseExpressionToBackend / parseExpressionFromBackend round-trip", () => {
  it("should restore original expression after to-backend then from-backend", () => {
    const original = "{{nodes.webhook.data.name}}";
    const backend = parseExpressionToBackend(original, nodesMap);
    const restored = parseExpressionFromBackend(backend, nodesMap);
    expect(restored).toBe(original);
  });
});

describe("extractNodeReferences", () => {
  it("should extract all node names from expression", () => {
    const refs = extractNodeReferences("{{nodes.webhook.id}} and {{nodes.sendEmail.to}}");
    expect(refs).toContain("webhook");
    expect(refs).toContain("sendEmail");
    expect(refs).toHaveLength(2);
  });

  it("should deduplicate repeated node references", () => {
    const refs = extractNodeReferences("{{nodes.webhook.a}} + {{nodes.webhook.b}}");
    expect(refs).toEqual(["webhook"]);
  });

  it("should return empty array for empty string", () => {
    expect(extractNodeReferences("")).toEqual([]);
  });

  it("should return empty array for plain text with no expressions", () => {
    expect(extractNodeReferences("plain text")).toEqual([]);
  });
});

describe("validateExpression", () => {
  it("should return isValid: true when all referenced nodes exist", () => {
    const result = validateExpression("{{nodes.webhook.name}}", nodesMap);
    expect(result.isValid).toBe(true);
    expect(result.errors).toHaveLength(0);
  });

  it("should return isValid: false with an error for missing node", () => {
    const result = validateExpression("{{nodes.missingNode.name}}", nodesMap);
    expect(result.isValid).toBe(false);
    expect(result.errors[0]).toContain("missingNode");
  });

  it("should return isValid: true for empty expression", () => {
    const result = validateExpression("", nodesMap);
    expect(result.isValid).toBe(true);
    expect(result.errors).toHaveLength(0);
  });
});

describe("getNodeBackendRef", () => {
  it("should return backend ref string for known node name", () => {
    const ref = getNodeBackendRef("webhook", nodesMap);
    expect(ref).toBe("node_node-trigger-001_trigger");
  });

  it("should return null for unknown node name", () => {
    const ref = getNodeBackendRef("unknown", nodesMap);
    expect(ref).toBeNull();
  });
});

describe("getNodeAlias", () => {
  it("should return node name for known node ID", () => {
    const alias = getNodeAlias("node-trigger-001", nodesMap);
    expect(alias).toBe("webhook");
  });

  it("should return null for unknown node ID", () => {
    const alias = getNodeAlias("nonexistent-id", nodesMap);
    expect(alias).toBeNull();
  });
});
