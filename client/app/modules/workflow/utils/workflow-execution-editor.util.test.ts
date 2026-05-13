import { describe, it, expect } from "vitest";
import { NodeExecutionStatus, getStatusStyles } from "./workflow-execution-editor.util";

describe("getStatusStyles", () => {
  it("should return yellow styles for Pending status", () => {
    const result = getStatusStyles(NodeExecutionStatus.Pending);
    expect(result.nodeClass).toBe("border-yellow-500");
    expect(result.edgeColor).toBe("#eab308");
    expect(result.edgeClass).toBe("stroke-yellow-500");
  });

  it("should return purple styles for Running status", () => {
    const result = getStatusStyles(NodeExecutionStatus.Running);
    expect(result.nodeClass).toBe("border-purple-500");
    expect(result.edgeColor).toBe("#a855f7");
    expect(result.edgeClass).toBe("stroke-purple-500");
  });

  it("should return success styles for Completed status", () => {
    const result = getStatusStyles(NodeExecutionStatus.Completed);
    expect(result.nodeClass).toBe("border-success");
    expect(result.edgeColor).toBe("#18c964");
    expect(result.edgeClass).toBe("stroke-success");
  });

  it("should return destructive styles for Failed status", () => {
    const result = getStatusStyles(NodeExecutionStatus.Failed);
    expect(result.nodeClass).toBe("border-destructive");
    expect(result.edgeColor).toBe("#ef4444");
    expect(result.edgeClass).toBe("stroke-destructive");
  });

  it("should return neutral default styles for an unknown status", () => {
    const result = getStatusStyles(999);
    expect(result.nodeClass).toBe("");
    expect(result.edgeColor).toBe("#94a3b8");
    expect(result.edgeClass).toBe("");
  });

  it("NodeExecutionStatus enum should have the correct numeric values", () => {
    expect(NodeExecutionStatus.Pending).toBe(2);
    expect(NodeExecutionStatus.Running).toBe(3);
    expect(NodeExecutionStatus.Completed).toBe(4);
    expect(NodeExecutionStatus.Failed).toBe(5);
  });
});
