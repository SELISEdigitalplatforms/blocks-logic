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
  lastRunAt: "2026-09-01T00:00:00.000Z",
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
  lastRunAt: null,
};

describe("FunctionsTable", () => {
  it("shows the create-function empty state when there are no functions", () => {
    renderWithProviders(
      <MemoryRouter>
        <FunctionsTable functions={[]} isLoading={false} onCreateFunction={vi.fn()} />
      </MemoryRouter>,
    );
    expect(screen.getByText(/create your first function/i)).toBeTruthy();
  });

  it("renders each function's name, id, status and version", () => {
    renderWithProviders(
      <MemoryRouter>
        <FunctionsTable functions={[live, draft]} isLoading={false} onCreateFunction={vi.fn()} />
      </MemoryRouter>,
    );

    expect(screen.getByText("Send confirmation")).toBeTruthy();
    expect(screen.getByText("fn_1")).toBeTruthy();
    expect(screen.getByText("Live")).toBeTruthy();
    expect(screen.getByText("v3")).toBeTruthy();

    expect(screen.getByText("Sync inventory")).toBeTruthy();
    expect(screen.getByText("Draft")).toBeTruthy();
  });

  it("flags a Live function with unsaved changes", () => {
    renderWithProviders(
      <MemoryRouter>
        <FunctionsTable functions={[{ ...live, isDirty: true }]} isLoading={false} onCreateFunction={vi.fn()} />
      </MemoryRouter>,
    );
    expect(screen.getByText(/unpublished changes/i)).toBeTruthy();
  });
});
