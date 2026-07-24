import React from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
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
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
  showInfoToast: vi.fn(),
}));

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
  it("opens the publish confirmation for an unpublished, clean workflow", async () => {
    const user = userEvent.setup();
    wrap(<PublishWorkflowAction isPublished={false} isDirty={false} />);
    await user.click(screen.getByRole("button", { name: /Publish/ }));
    await user.click(await screen.findByRole("menuitem", { name: "Publish" }));
    await waitFor(() =>
      expect(document.querySelector("[role='dialog']")).toBeTruthy(),
    );
  });

  it("opens the versioned publish dialog for a dirty workflow", async () => {
    const user = userEvent.setup();
    wrap(<PublishWorkflowAction isPublished={false} isDirty={true} />);
    await user.click(screen.getByRole("button", { name: /Publish/ }));
    await user.click(await screen.findByRole("menuitem", { name: "Publish" }));
    await waitFor(() =>
      expect(document.querySelector("[role='dialog']")).toBeTruthy(),
    );
  });

  it("opens the unpublish dialog for a published workflow", async () => {
    const user = userEvent.setup();
    wrap(<PublishWorkflowAction isPublished isDirty={false} />);
    await user.click(screen.getByRole("button", { name: /Publish/ }));
    await user.click(await screen.findByRole("menuitem", { name: "Unpublish" }));
    await waitFor(() =>
      expect(document.querySelector("[role='dialog']")).toBeTruthy(),
    );
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

  it("opens the edit-version modal", async () => {
    const user = userEvent.setup();
    wrap(
      <WorkflowVersionActionDropdown version={version}>
        <button>Menu</button>
      </WorkflowVersionActionDropdown>,
    );
    await user.click(screen.getByText("Menu"));
    await user.click(await screen.findByText("Edit version details"));
    await waitFor(() =>
      expect(document.querySelector("[role='dialog']")).toBeTruthy(),
    );
  });

  it("restores a version", async () => {
    const user = userEvent.setup();
    wrap(
      <WorkflowVersionActionDropdown version={version}>
        <button>Menu</button>
      </WorkflowVersionActionDropdown>,
    );
    await user.click(screen.getByText("Menu"));
    await user.click(await screen.findByText("Restore version"));
    await waitFor(() => expect(svc.restoreWorkflow).toHaveBeenCalled());
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
