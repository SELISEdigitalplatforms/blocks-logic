import React from "react";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router";
import { TooltipProvider } from "@/components/ui-kits/tooltip/tooltip";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const navigate = vi.hoisted(() => vi.fn());
vi.mock("react-router", async (orig) => {
  const actual = (await orig()) as Record<string, unknown>;
  return { ...actual, useNavigate: () => navigate };
});
const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
  showInfoToast: vi.fn(),
}));
vi.mock("@/hooks/use-toast", () => toasts);
const svc = vi.hoisted(() => ({
  getWorkflowById: vi.fn(),
  getLastSuccessfulExecution: vi.fn().mockResolvedValue(null),
  updateWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
  duplicateWorkflow: vi.fn().mockResolvedValue({ isSuccess: true, itemId: "NEW" }),
  deleteWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
}));
vi.mock("@/modules/workflow/services/workflow.service", () => ({
  workflowService: svc,
}));
const exportHook = vi.hoisted(() => ({
  exportWorkflow: vi.fn(),
  isExporting: false,
}));
vi.mock("../../hooks/use-export-workflow", () => ({
  useExportWorkflow: () => exportHook,
}));
// The editor canvas and the other tabs are covered by their own tests.
vi.mock("../../components/workflow-editor", () => ({ WorkflowEditor: () => null }));
vi.mock("../../components/workflow-version", () => ({ WorkflowVersions: () => null }));
vi.mock("../../components/publish-workflow-action", () => ({ PublishWorkflowAction: () => null }));
vi.mock("@blocks-workflow/components/workflow-execution", () => ({ WorkflowExecutions: () => null }));

import { WorkflowDetails } from "./workflow-details";

const PATH = "/p1/app/workflow/wf1";

const workflow = (extra: Record<string, unknown> = {}) => ({
  isSuccess: true,
  data: {
    itemId: "wf1",
    name: "Invoice sync",
    description: "",
    settings: {},
    nodes: [],
    edges: [],
    isDirty: false,
    isPublished: false,
    ...extra,
  },
});

const renderPage = () => {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[PATH]}>
        <TooltipProvider>
          <Routes>
            <Route path="/:scope/app/workflow/:id" element={<WorkflowDetails />} />
          </Routes>
        </TooltipProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
};

const openMenu = async (user: ReturnType<typeof userEvent.setup>) => {
  await screen.findByRole("button", { name: /save/i });
  const trigger = document.querySelector('[aria-haspopup="menu"]') as HTMLElement;
  await user.click(trigger);
  return screen.findByRole("menu");
};

beforeEach(() => {
  vi.clearAllMocks();
  svc.getWorkflowById.mockResolvedValue(workflow());
});
afterEach(() => {
  document.body.style.pointerEvents = "";
});

describe("WorkflowDetails", () => {
  it("shows the workflow name as the last breadcrumb item", async () => {
    renderPage();
    expect(await screen.findByText("Invoice sync")).toBeTruthy();
    expect(screen.getByRole("link", { name: "Workflow" })).toBeTruthy();
    expect(BREADCRUMB_CUSTOM_TITLES[PATH]).toBeUndefined();
  });

  it("falls back to the path label when the workflow has no name", async () => {
    svc.getWorkflowById.mockResolvedValue(workflow({ name: "" }));
    renderPage();
    await screen.findByRole("button", { name: /save/i });
    expect(screen.getByText("Wf1")).toBeTruthy();
    expect(BREADCRUMB_CUSTOM_TITLES[PATH]).toBeUndefined();
  });

  it("lists rename, duplicate, export and delete in the actions menu", async () => {
    const user = userEvent.setup();
    renderPage();
    const menu = await openMenu(user);
    const items = within(menu).getAllByRole("menuitem").map((i) => i.textContent);
    expect(items).toEqual(["Rename", "Duplicate", "Export", "Delete"]);
  });

  it("exports the current workflow", async () => {
    const user = userEvent.setup();
    renderPage();
    const menu = await openMenu(user);
    await user.click(within(menu).getByRole("menuitem", { name: "Export" }));
    expect(exportHook.exportWorkflow).toHaveBeenCalledWith("wf1");
  });

  it("disables export while an export is running", async () => {
    exportHook.isExporting = true;
    try {
      const user = userEvent.setup();
      renderPage();
      const menu = await openMenu(user);
      expect(within(menu).getByRole("menuitem", { name: "Export" }).getAttribute("aria-disabled")).toBe("true");
    } finally {
      exportHook.isExporting = false;
    }
  });

  it("renames the workflow from a prefilled modal", async () => {
    const user = userEvent.setup();
    renderPage();
    const menu = await openMenu(user);
    await user.click(within(menu).getByRole("menuitem", { name: "Rename" }));

    const input = await screen.findByPlaceholderText("Enter workflow name");
    expect((input as HTMLInputElement).value).toBe("Invoice sync");
    await user.clear(input);
    await user.type(input, "Invoice sync v2");
    await user.click(screen.getByRole("button", { name: "Rename" }));

    await waitFor(() => expect(svc.updateWorkflow).toHaveBeenCalled());
    expect(svc.updateWorkflow.mock.calls[0][0]).toEqual({ itemId: "wf1", name: "Invoice sync v2" });
    expect(toasts.showSuccessToast).toHaveBeenCalledWith({ description: "Workflow successfully renamed." });
  });

  it("does not rename to an empty name", async () => {
    const user = userEvent.setup();
    renderPage();
    const menu = await openMenu(user);
    await user.click(within(menu).getByRole("menuitem", { name: "Rename" }));

    const input = await screen.findByPlaceholderText("Enter workflow name");
    await user.clear(input);
    await user.type(input, "   ");
    await user.click(screen.getByRole("button", { name: "Rename" }));

    expect(await screen.findByText("Workflow name is required")).toBeTruthy();
    expect(svc.updateWorkflow).not.toHaveBeenCalled();
  });

  it("closes the rename modal on Escape without saving", async () => {
    const user = userEvent.setup();
    renderPage();
    const menu = await openMenu(user);
    await user.click(within(menu).getByRole("menuitem", { name: "Rename" }));
    await screen.findByPlaceholderText("Enter workflow name");

    await user.keyboard("{Escape}");

    await waitFor(() => expect(screen.queryByPlaceholderText("Enter workflow name")).toBeNull());
    expect(svc.updateWorkflow).not.toHaveBeenCalled();
  });

  it("duplicates the workflow and opens the copy", async () => {
    const user = userEvent.setup();
    renderPage();
    const menu = await openMenu(user);
    await user.click(within(menu).getByRole("menuitem", { name: "Duplicate" }));

    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(navigate).toHaveBeenCalledWith("workflow/NEW"));
    expect(svc.duplicateWorkflow.mock.calls[0][0]).toMatchObject({ workflowId: "wf1" });
  });

  it("goes back to the scoped workflow list after a delete", async () => {
    const user = userEvent.setup();
    renderPage();
    const menu = await openMenu(user);
    await user.click(within(menu).getByRole("menuitem", { name: "Delete" }));
    await user.click(await screen.findByRole("button", { name: "Yes" }));

    await waitFor(() => expect(navigate).toHaveBeenCalledWith("workflow"));
    expect(svc.deleteWorkflow.mock.calls[0][0]).toEqual({ id: "wf1" });
    expect(navigate).not.toHaveBeenCalledWith("/app/workflow");
  });

  it("stays on the page when the delete fails", async () => {
    svc.deleteWorkflow.mockResolvedValueOnce({ isSuccess: false, errors: "Not allowed" });
    const user = userEvent.setup();
    renderPage();
    const menu = await openMenu(user);
    await user.click(within(menu).getByRole("menuitem", { name: "Delete" }));
    await user.click(await screen.findByRole("button", { name: "Yes" }));

    await waitFor(() => expect(toasts.showErrorToast).toHaveBeenCalledWith({ errors: "Not allowed" }));
    expect(navigate).not.toHaveBeenCalled();
  });
});
