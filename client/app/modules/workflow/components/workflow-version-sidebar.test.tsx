import React from "react";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router";
import { NuqsTestingAdapter } from "nuqs/adapters/testing";
import { beforeEach, describe, expect, it, vi } from "vitest";

const svc = vi.hoisted(() => ({
  getWorkflowVersions: vi.fn(),
  restoreWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
  updateWorkflowVersion: vi.fn().mockResolvedValue({ isSuccess: true }),
  publishWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
  unpublishWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
  getWorkflows: vi.fn().mockResolvedValue({ data: [], totalCount: 0 }),
}));
vi.mock("@/modules/workflow/services/workflow.service", () => ({
  workflowService: svc,
}));

import { VersionHistorySidebar } from "./version-history-sidebar/version-history-sidebar";
import { WorkflowFilterToolBar } from "./workflow-filter-toolbar/workflow-filter-toolbar";

const wrapRouter = (ui: React.ReactElement) => {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false } },
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

describe("VersionHistorySidebar", () => {
  it("shows the empty state when there are no versions", async () => {
    svc.getWorkflowVersions.mockResolvedValue({ data: [] });
    wrapRouter(<VersionHistorySidebar onSelectVersion={vi.fn()} />);
    await waitFor(() => expect(screen.getByText("No versions found.")).toBeTruthy());
  });

  it("lists versions and selects one on click", async () => {
    svc.getWorkflowVersions.mockResolvedValue({
      data: [
        {
          itemId: "v1",
          name: "Version 1",
          description: "first",
          isPublished: true,
          lastUpdatedDate: "2023-01-01T00:00:00Z",
        },
      ],
    });
    const onSelect = vi.fn();
    wrapRouter(<VersionHistorySidebar onSelectVersion={onSelect} selectedVersionId="v1" />);
    await waitFor(() => expect(screen.getByText("Version 1")).toBeTruthy());
    screen.getByText("Version 1").click();
    expect(onSelect).toHaveBeenCalled();
  });
});

describe("WorkflowFilterToolBar", () => {
  it("renders the search and status filters", () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter>
          <NuqsTestingAdapter>
            <WorkflowFilterToolBar />
          </NuqsTestingAdapter>
        </MemoryRouter>
      </QueryClientProvider>,
    );
    // the search input from the toolbar renders
    expect(screen.getAllByPlaceholderText("Search...").length).toBeGreaterThan(0);
  });
});
