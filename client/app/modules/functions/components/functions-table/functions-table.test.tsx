import { describe, it, expect, vi } from "vitest";
import { screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { FunctionsTable } from "./functions-table";
import { IFunctionSummary } from "../../types/function.types";

const live: IFunctionSummary = {
  id: "fn_1",
  name: "Send confirmation",
  status: "Live",
  isDirty: false,
  activeVersionNumber: 3,
  totalRuns: 42,
  runs24h: 1204,
  httpEnabled: true,
  workflowEnabled: true,
  lastRunAt: new Date(Date.now() - 3 * 60_000).toISOString(),
  lastDeployedAt: "2026-08-30T00:00:00.000Z",
  lastUpdatedDate: "2026-09-01T00:00:00.000Z",
};

const draft: IFunctionSummary = {
  ...live,
  id: "fn_2",
  name: "Sync inventory",
  status: "Draft",
  activeVersionNumber: null,
  totalRuns: 0,
  runs24h: 0,
  workflowEnabled: false,
  lastRunAt: null,
};

const render = (functions: IFunctionSummary[], props: Partial<{ hasFilters: boolean }> = {}) =>
  renderWithProviders(
    <MemoryRouter>
      <FunctionsTable
        functions={functions}
        isLoading={false}
        onCreateFunction={vi.fn()}
        {...props}
      />
    </MemoryRouter>,
  );

describe("FunctionsTable", () => {
  it("explains what a function is when the project has none", () => {
    render([]);
    expect(screen.getByText(/no functions yet/i)).toBeTruthy();
    expect(screen.getByRole("button", { name: /new function/i })).toBeTruthy();
  });

  it("distinguishes an empty search from an empty project", () => {
    render([], { hasFilters: true });
    expect(screen.getByText(/no functions match this search/i)).toBeTruthy();
    expect(screen.queryByText(/no functions yet/i)).toBeNull();
  });

  it("renders the design's columns for each function", () => {
    render([live, draft]);

    expect(screen.getByText("Send confirmation")).toBeTruthy();
    // The endpoint, not the bare id — it is what a caller needs.
    expect(screen.getByText("/api/fn/fn_1")).toBeTruthy();
    expect(screen.getByText("Live")).toBeTruthy();
    expect(screen.getByText("v3")).toBeTruthy();
    expect(screen.getByText("1,204")).toBeTruthy();
    expect(screen.getByText("3 min ago")).toBeTruthy();

    expect(screen.getByText("Sync inventory")).toBeTruthy();
    expect(screen.getByText("Draft")).toBeTruthy();
    // Never deployed and no runs.
    expect(screen.getAllByText("—").length).toBeGreaterThan(0);
  });

  it("shows how each function can be invoked", () => {
    render([live, draft]);
    expect(screen.getByText("HTTP · Workflow")).toBeTruthy();
    expect(screen.getByText("HTTP")).toBeTruthy();
  });

  it("flags a Live function with unsaved changes", () => {
    render([{ ...live, isDirty: true }]);
    expect(screen.getByText(/unpublished changes/i)).toBeTruthy();
  });
});
