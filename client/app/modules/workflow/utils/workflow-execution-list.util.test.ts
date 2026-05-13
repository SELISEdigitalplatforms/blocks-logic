import { describe, it, expect } from "vitest";
import { WorkflowExecutionStatus, getStatusConfig } from "./workflow-execution-list.util";

describe("getStatusConfig", () => {
  it("should return gray/Initialized for Init status", () => {
    const result = getStatusConfig(WorkflowExecutionStatus.Init);
    expect(result.label).toBe("Initialized");
    expect(result.color).toBe("bg-gray-400");
    expect(result.textClass).toBe("text-gray-600");
  });

  it("should return blue/Queued for Queued status", () => {
    const result = getStatusConfig(WorkflowExecutionStatus.Queued);
    expect(result.label).toBe("Queued");
    expect(result.color).toBe("bg-blue-400");
    expect(result.textClass).toBe("text-blue-600");
  });

  it("should return yellow/Pending for Pending status", () => {
    const result = getStatusConfig(WorkflowExecutionStatus.Pending);
    expect(result.label).toBe("Pending");
    expect(result.color).toBe("bg-yellow-400");
    expect(result.textClass).toBe("text-yellow-600");
  });

  it("should return purple/Running for Running status", () => {
    const result = getStatusConfig(WorkflowExecutionStatus.Running);
    expect(result.label).toBe("Running");
    expect(result.color).toBe("bg-purple-400");
    expect(result.textClass).toBe("text-purple-600");
  });

  it("should return success/Completed for Completed status", () => {
    const result = getStatusConfig(WorkflowExecutionStatus.Completed);
    expect(result.label).toBe("Completed");
    expect(result.color).toBe("bg-success");
    expect(result.textClass).toBe("text-success");
  });

  it("should return error/Failed for Failed status", () => {
    const result = getStatusConfig(WorkflowExecutionStatus.Failed);
    expect(result.label).toBe("Failed");
    expect(result.color).toBe("bg-error");
    expect(result.textClass).toBe("text-error");
  });

  it("should return gray/Unknown for an unrecognised status code", () => {
    const result = getStatusConfig(999);
    expect(result.label).toBe("Unknown");
    expect(result.color).toBe("bg-gray-400");
    expect(result.textClass).toBe("text-gray-600");
  });

  it("WorkflowExecutionStatus enum should have the correct numeric values", () => {
    expect(WorkflowExecutionStatus.Init).toBe(0);
    expect(WorkflowExecutionStatus.Queued).toBe(1);
    expect(WorkflowExecutionStatus.Pending).toBe(2);
    expect(WorkflowExecutionStatus.Running).toBe(3);
    expect(WorkflowExecutionStatus.Completed).toBe(4);
    expect(WorkflowExecutionStatus.Failed).toBe(5);
  });
});
