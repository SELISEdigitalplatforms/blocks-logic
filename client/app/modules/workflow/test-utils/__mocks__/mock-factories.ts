import { vi } from "vitest";

export const mockWorkflowServiceFactory = () => ({
  workflowService: {
    getWorkflows: vi.fn(),
    getWorkflowById: vi.fn(),
    createWorkflow: vi.fn(),
    duplicateWorkflow: vi.fn(),
    updateWorkflow: vi.fn(),
    deleteWorkflow: vi.fn(),
    getWorkflowExecutions: vi.fn(),
    getWorkflowExecutionById: vi.fn(),
  },
});
