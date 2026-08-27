import React from "react";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const svc = vi.hoisted(() => ({
  publishWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
  publishWorkflowNewVersion: vi.fn().mockResolvedValue({ isSuccess: true }),
  unpublishWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
  restoreWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
  updateWorkflowVersion: vi.fn().mockResolvedValue({ isSuccess: true }),
}));
vi.mock("@/modules/workflow/services/workflow.service", () => ({
  workflowService: svc,
}));
const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
  showInfoToast: vi.fn(),
}));
vi.mock("@/hooks/use-toast", () => toasts);

import { PublishWorkflowAction } from "./publish-workflow-action/publish-workflow-action";
import { WorkflowVersionActionDropdown } from "./workflow-version/workflow-version-action-dropdown";

const wrap = (ui: React.ReactElement) => {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={["/workflow/w1"]}>
        <Routes>
          <Route path="/workflow/:id" element={ui} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
};

beforeEach(() => vi.clearAllMocks());
// Radix dialogs set body pointer-events:none while open; if a test ends with a
// dialog open, reset it so the next test's clicks are not blocked.
afterEach(() => {
  document.body.style.pointerEvents = "";
});

describe("PublishWorkflowAction", () => {
  it("publishes an unpublished, clean workflow from the confirmation", async () => {
    const user = userEvent.setup();
    wrap(<PublishWorkflowAction isPublished={false} isDirty={false} />);
    await user.click(screen.getByRole("button", { name: /Publish/ }));
    await user.click(await screen.findByRole("menuitem", { name: "Publish" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Publish" }));
    await waitFor(() => expect(svc.publishWorkflow).toHaveBeenCalled());
  });

  it("publishes a new version from the dirty-workflow dialog", async () => {
    const user = userEvent.setup();
    wrap(<PublishWorkflowAction isPublished={false} isDirty={true} />);
    await user.click(screen.getByRole("button", { name: /Publish/ }));
    await user.click(await screen.findByRole("menuitem", { name: "Publish" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Publish" }));
    await waitFor(() =>
      expect(svc.publishWorkflowNewVersion).toHaveBeenCalled(),
    );
  });

  it("unpublishes a published workflow from the confirmation", async () => {
    const user = userEvent.setup();
    wrap(<PublishWorkflowAction isPublished isDirty={false} />);
    await user.click(screen.getByRole("button", { name: /Publish/ }));
    await user.click(await screen.findByRole("menuitem", { name: "Unpublish" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Unpublish" }));
    await waitFor(() => expect(svc.unpublishWorkflow).toHaveBeenCalled());
  });
});

describe("WorkflowVersionActionDropdown", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const version: any = {
    itemId: "v1",
    name: "Version 1",
    description: "desc",
    isPublished: false,
  };

  it("edits a version's details and saves", async () => {
    const user = userEvent.setup();
    wrap(
      <WorkflowVersionActionDropdown version={version}>
        <button>Menu</button>
      </WorkflowVersionActionDropdown>,
    );
    await user.click(screen.getByText("Menu"));
    await user.click(await screen.findByText("Edit version details"));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /Save changes/ }));
    await waitFor(() => expect(svc.updateWorkflowVersion).toHaveBeenCalled());
  });

  it("publishes an unpublished version from the confirmation", async () => {
    const user = userEvent.setup();
    wrap(
      <WorkflowVersionActionDropdown version={version}>
        <button>Menu</button>
      </WorkflowVersionActionDropdown>,
    );
    await user.click(screen.getByText("Menu"));
    await user.click(await screen.findByText("Publish version"));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Publish" }));
    await waitFor(() => expect(svc.publishWorkflow).toHaveBeenCalled());
  });

  it("unpublishes a published version from the confirmation", async () => {
    const user = userEvent.setup();
    wrap(
      <WorkflowVersionActionDropdown version={{ ...version, isPublished: true }}>
        <button>Menu</button>
      </WorkflowVersionActionDropdown>,
    );
    await user.click(screen.getByText("Menu"));
    await user.click(await screen.findByText("Unpublish version"));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Unpublish" }));
    await waitFor(() => expect(svc.unpublishWorkflow).toHaveBeenCalled());
  });

  it("restores a version and shows success toast", async () => {
    const user = userEvent.setup();
    wrap(
      <WorkflowVersionActionDropdown version={version}>
        <button>Menu</button>
      </WorkflowVersionActionDropdown>,
    );
    await user.click(screen.getByText("Menu"));
    await user.click(await screen.findByText("Restore version"));
    await waitFor(() => expect(svc.restoreWorkflow).toHaveBeenCalled());
    await waitFor(() => expect(toasts.showSuccessToast).toHaveBeenCalledWith({
      description: "Workflow version successfully restored.",
    }));
  });

  it("shows error toast when restoring a version fails", async () => {
    svc.restoreWorkflow.mockRejectedValueOnce(new Error("Restore failed"));
    const user = userEvent.setup();
    wrap(
      <WorkflowVersionActionDropdown version={version}>
        <button>Menu</button>
      </WorkflowVersionActionDropdown>,
    );
    await user.click(screen.getByText("Menu"));
    await user.click(await screen.findByText("Restore version"));
    await waitFor(() => expect(toasts.showErrorToast).toHaveBeenCalledWith({
      errors: "Restore failed",
    }));
  });

  it("opens the publish confirmation for an unpublished version", async () => {
    const user = userEvent.setup();
    wrap(
      <WorkflowVersionActionDropdown version={version}>
        <button>Menu</button>
      </WorkflowVersionActionDropdown>,
    );
    await user.click(screen.getByText("Menu"));
    await user.click(await screen.findByText("Publish version"));
    await waitFor(() =>
      expect(document.querySelector("[role='dialog']")).toBeTruthy(),
    );
  });

  it("offers unpublish for a published version", async () => {
    const user = userEvent.setup();
    wrap(
      <WorkflowVersionActionDropdown version={{ ...version, isPublished: true }}>
        <button>Menu</button>
      </WorkflowVersionActionDropdown>,
    );
    await user.click(screen.getByText("Menu"));
    expect(await screen.findByText("Unpublish version")).toBeTruthy();
  });
});
