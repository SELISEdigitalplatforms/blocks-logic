import React from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const api = vi.hoisted(() => ({
  updateSettings: vi.fn(),
  getWorkflows: vi.fn(),
  getWorkflowById: vi.fn(),
}));
const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
  showInfoToast: vi.fn(),
}));

vi.mock("@blocks-workflow/hooks/use-workflow-api", () => ({
  useUpdateWorkflowSettings: () => ({ isPending: false, mutateAsync: api.updateSettings }),
  useGetWorkflows: (payload: unknown) => api.getWorkflows(payload),
  useGetWorkflowById: (payload: { id: string }) => api.getWorkflowById(payload),
}));
vi.mock("@/hooks/use-toast", () => toasts);

import { WorkflowSettings } from "./workflow-settings";
import { WorkflowAlertMode, WorkflowSettings as Settings } from "@blocks-workflow/models/workflow.model";

const onOpenChange = vi.fn();
const renderModal = (settings: Settings | null) =>
  render(<WorkflowSettings open onOpenChange={onOpenChange} workflowId="wf-a" settings={settings} />);

beforeEach(() => {
  vi.clearAllMocks();
  api.getWorkflows.mockReturnValue({
    data: { data: [{ itemId: "wf-b", name: "Ops alerts" }, { itemId: "wf-c", name: "Billing alerts" }], totalCount: 2 },
    isLoading: false,
  });
  api.getWorkflowById.mockImplementation(({ id }: { id: string }) => ({
    data: id === "wf-b" ? { data: { name: "Ops alerts" } } : undefined,
    isLoading: false,
  }));
});

describe("WorkflowSettings", () => {
  it("H2: shows the title, the Alert on failure row and Save only; the workflow row is hidden while off", () => {
    renderModal(null);
    expect(screen.getByText("Workflow settings")).toBeTruthy();
    expect(screen.getByRole("switch", { name: "Alert on failure" }).getAttribute("aria-checked")).toBe("false");
    expect(screen.queryByText("Error workflow")).toBeNull();
    expect(screen.getByRole("button", { name: "Save" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Cancel" })).toBeNull();
  });

  it("H3: an enabled alert shows its target", () => {
    renderModal({ alert: { isEnabled: true, mode: WorkflowAlertMode.Workflow, workflowId: "wf-b" } });
    expect(screen.getByRole("combobox").textContent).toContain("Ops alerts");
  });

  it("H4: the picker asks for published workflows with an Error Trigger, without this one", () => {
    renderModal({ alert: { isEnabled: true, mode: WorkflowAlertMode.Workflow, workflowId: null } });
    expect(api.getWorkflows).toHaveBeenCalledWith(
      expect.objectContaining({ isPublished: true, hasErrorTrigger: true, excludeItemId: "wf-a", pageSize: 50 }),
    );
  });

  it("H5: turning the alert on, choosing a workflow and saving sends the alert", async () => {
    api.updateSettings.mockResolvedValue({ isSuccess: true });
    renderModal(null);
    fireEvent.click(screen.getByRole("switch", { name: "Alert on failure" }));
    fireEvent.click(screen.getByRole("combobox"));
    fireEvent.click(await screen.findByText("Billing alerts"));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(api.updateSettings).toHaveBeenCalledWith({
        itemId: "wf-a",
        settings: { alert: { isEnabled: true, mode: 1, workflowId: "wf-c" } },
      }),
    );
    expect(toasts.showSuccessToast).toHaveBeenCalledWith({ description: "Settings saved." });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("H7/C17: turning it off hides the row and saves the kept target as disabled", async () => {
    api.updateSettings.mockResolvedValue({ isSuccess: true });
    renderModal({ alert: { isEnabled: true, mode: WorkflowAlertMode.Workflow, workflowId: "wf-b" } });
    fireEvent.click(screen.getByRole("switch", { name: "Alert on failure" }));
    expect(screen.queryByText("Error workflow")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(api.updateSettings).toHaveBeenCalledWith({
        itemId: "wf-a",
        settings: { alert: { isEnabled: false, mode: 1, workflowId: "wf-b" } },
      }),
    );
  });

  it("an enabled alert without a workflow is not saved", async () => {
    renderModal(null);
    fireEvent.click(screen.getByRole("switch", { name: "Alert on failure" }));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(await screen.findByText("Select a workflow", { selector: "p" })).toBeTruthy();
    expect(api.updateSettings).not.toHaveBeenCalled();
  });

  it("C16: a server error on the target shows under the field", async () => {
    api.updateSettings.mockResolvedValue({
      isSuccess: false,
      errors: { "Settings.Alert.WorkflowId": "The selected workflow isn't published." },
    });
    renderModal({ alert: { isEnabled: true, mode: WorkflowAlertMode.Workflow, workflowId: "wf-b" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(await screen.findByText("The selected workflow isn't published.")).toBeTruthy();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it("C16: a deleted target shows as unavailable", () => {
    renderModal({ alert: { isEnabled: true, mode: WorkflowAlertMode.Workflow, workflowId: "wf-gone" } });
    expect(screen.getByRole("combobox").textContent).toContain("Unavailable workflow");
  });
});
