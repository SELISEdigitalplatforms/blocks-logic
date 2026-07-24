import React from "react";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { TooltipProvider } from "@/components/ui-kits/tooltip/tooltip";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const navigate = vi.hoisted(() => vi.fn());
vi.mock("react-router-dom", async (orig) => {
  const actual = (await orig()) as Record<string, unknown>;
  return { ...actual, useNavigate: () => navigate };
});
const svc = vi.hoisted(() => ({
  deleteWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
  duplicateWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
  updateWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
  publishWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
  publishWorkflowNewVersion: vi.fn().mockResolvedValue({ isSuccess: true }),
  unpublishWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
}));
vi.mock("@/modules/workflow/services/workflow.service", () => ({
  workflowService: svc,
}));

import { WorkflowList } from "./workflow-list";

const wf = (id: string, extra: Record<string, unknown> = {}) => ({
  itemId: id,
  name: `Flow ${id}`,
  createdDate: "2023-01-01T00:00:00Z",
  lastUpdatedDate: "2023-02-01T00:00:00Z",
  isPublished: false,
  isDirty: false,
  ...extra,
});

const wrap = (ui: React.ReactElement) => {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <TooltipProvider>{ui}</TooltipProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
};

beforeEach(() => vi.clearAllMocks());
afterEach(() => {
  document.body.style.pointerEvents = "";
});

describe("WorkflowList", () => {
  it("renders a skeleton while loading", () => {
    const { container } = wrap(<WorkflowList workflow={[]} isLoading />);
    expect(container.querySelectorAll("tbody tr").length).toBeGreaterThan(0);
  });

  it("shows the empty state", () => {
    wrap(<WorkflowList workflow={[]} isLoading={false} />);
    expect(screen.getByText("No results found.")).toBeTruthy();
  });

  it("renders workflow rows with name and status", () => {
    wrap(
      <WorkflowList
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        workflow={[wf("1"), wf("2", { isPublished: true }) as any] as any}
        isLoading={false}
      />,
    );
    expect(screen.getByText("Flow 1")).toBeTruthy();
    expect(screen.getByText("Published")).toBeTruthy();
    expect(screen.getByText("Unpublished")).toBeTruthy();
  });

  it("navigates when a row is clicked", () => {
    wrap(
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      <WorkflowList workflow={[wf("9") as any]} isLoading={false} />,
    );
    fireEvent.click(screen.getByText("Flow 9"));
    expect(navigate).toHaveBeenCalledWith("workflow/9");
  });

  it("opens the delete dialog from the row menu", async () => {
    const user = userEvent.setup();
    wrap(
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      <WorkflowList workflow={[wf("5") as any]} isLoading={false} />,
    );
    // open the actions dropdown (the ellipsis trigger)
    const triggers = screen.getAllByRole("button");
    await user.click(triggers[triggers.length - 1]);
    await user.click(await screen.findByText("Delete"));
    await waitFor(() => expect(screen.getByText("Delete Workflow")).toBeTruthy());
  });

  it("opens the rename dialog from the row menu", async () => {
    const user = userEvent.setup();
    wrap(
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      <WorkflowList workflow={[wf("6") as any]} isLoading={false} />,
    );
    const triggers = screen.getAllByRole("button");
    await user.click(triggers[triggers.length - 1]);
    await user.click(await screen.findByText("Rename"));
    await waitFor(() => expect(screen.getByText("Rename workflow")).toBeTruthy());
  });

  it("opens the publish confirmation when toggling an unpublished workflow", () => {
    wrap(
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      <WorkflowList workflow={[wf("7") as any]} isLoading={false} />,
    );
    const toggle = screen.getByRole("switch");
    fireEvent.click(toggle);
    // a publish confirmation dialog opens
    expect(document.querySelector("[role='dialog']")).toBeTruthy();
  });

  it("publishes an unpublished clean workflow from the confirmation", async () => {
    wrap(
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      <WorkflowList workflow={[wf("8") as any]} isLoading={false} />,
    );
    fireEvent.click(screen.getByRole("switch"));
    fireEvent.click(await screen.findByText("Publish"));
    await waitFor(() => expect(svc.publishWorkflow).toHaveBeenCalled());
  });

  it("opens the versioned publish modal for a dirty workflow", () => {
    wrap(
      <WorkflowList
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        workflow={[wf("9", { isDirty: true }) as any]}
        isLoading={false}
      />,
    );
    fireEvent.click(screen.getByRole("switch"));
    expect(document.querySelector("[role='dialog']")).toBeTruthy();
  });

  it("unpublishes a published workflow from the confirmation", async () => {
    wrap(
      <WorkflowList
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        workflow={[wf("10", { isPublished: true }) as any]}
        isLoading={false}
      />,
    );
    fireEvent.click(screen.getByRole("switch"));
    fireEvent.click(await screen.findByText("Unpublish"));
    await waitFor(() => expect(svc.unpublishWorkflow).toHaveBeenCalled());
  });

  it("opens the duplicate dialog from the row menu", async () => {
    const user = userEvent.setup();
    wrap(
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      <WorkflowList workflow={[wf("11") as any]} isLoading={false} />,
    );
    const triggers = screen.getAllByRole("button");
    await user.click(triggers[triggers.length - 1]);
    await user.click(await screen.findByText("Duplicate"));
    await waitFor(() =>
      expect(screen.getByText("Duplicate workflow")).toBeTruthy(),
    );
  });
});
