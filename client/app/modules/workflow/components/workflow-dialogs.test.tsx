import React from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

const api = vi.hoisted(() => ({
  create: vi.fn(),
  update: vi.fn(),
  del: vi.fn(),
  duplicate: vi.fn(),
}));
const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
  showInfoToast: vi.fn(),
}));
const navigate = vi.hoisted(() => vi.fn());

vi.mock("@blocks-workflow/hooks/use-workflow-api", () => ({
  useCreateWorkflow: () => ({ isPending: false, mutateAsync: api.create }),
  useUpdateWorkflow: () => ({ isPending: false, mutateAsync: api.update }),
  useDeleteWorkflow: () => ({ isPending: false, mutateAsync: api.del }),
  useDuplicateWorkflow: () => ({ isPending: false, mutateAsync: api.duplicate }),
}));
vi.mock("@/hooks/use-toast", () => toasts);
vi.mock("react-router-dom", async (orig) => {
  const actual = (await orig()) as Record<string, unknown>;
  return { ...actual, useNavigate: () => navigate };
});

import { AddWorkflow } from "./add-workflow/add-workflow";
import { RenameWorkflow } from "./rename-workflow/rename-workflow";
import { DeleteWorkflow } from "./delete-workflow/delete-workflow";
import { DuplicateWorkflow } from "./duplicate-workflow/duplicate-workflow";

const wrap = (ui: React.ReactElement) =>
  render(<MemoryRouter>{ui}</MemoryRouter>);

beforeEach(() => vi.clearAllMocks());

describe("AddWorkflow", () => {
  it("opens the dialog, creates a workflow and navigates", async () => {
    api.create.mockResolvedValue({ isSuccess: true, itemId: "w1" });
    wrap(<AddWorkflow />);
    fireEvent.click(screen.getByText("Add Workflow"));
    fireEvent.change(screen.getByPlaceholderText("Enter workflow name"), {
      target: { value: "My Flow" },
    });
    fireEvent.click(screen.getByText("Create"));
    await waitFor(() =>
      expect(api.create).toHaveBeenCalledWith({ name: "My Flow" }),
    );
    await waitFor(() =>
      expect(navigate).toHaveBeenCalledWith("workflow/w1"),
    );
    expect(toasts.showSuccessToast).toHaveBeenCalled();
  });

  it("surfaces a server error", async () => {
    api.create.mockResolvedValue({ isSuccess: false, errors: { m: "bad" } });
    wrap(<AddWorkflow />);
    fireEvent.click(screen.getByText("Add Workflow"));
    fireEvent.change(screen.getByPlaceholderText("Enter workflow name"), {
      target: { value: "X" },
    });
    fireEvent.click(screen.getByText("Create"));
    await waitFor(() => expect(toasts.showErrorToast).toHaveBeenCalled());
  });
});

describe("RenameWorkflow", () => {
  it("renames a workflow", async () => {
    api.update.mockResolvedValue({ isSuccess: true });
    const onOpenChange = vi.fn();
    wrap(
      <RenameWorkflow
        open
        onOpenChange={onOpenChange}
        workflowId="w1"
        initialName="Old"
      />,
    );
    fireEvent.change(screen.getByDisplayValue("Old"), {
      target: { value: "New" },
    });
    fireEvent.click(screen.getByText("Rename"));
    await waitFor(() =>
      expect(api.update).toHaveBeenCalledWith({ itemId: "w1", name: "New" }),
    );
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("handles a thrown error", async () => {
    api.update.mockRejectedValue(new Error("nope"));
    wrap(
      <RenameWorkflow open onOpenChange={vi.fn()} workflowId="w1" initialName="A" />,
    );
    fireEvent.click(screen.getByText("Rename"));
    await waitFor(() => expect(toasts.showErrorToast).toHaveBeenCalled());
  });
});

describe("DeleteWorkflow", () => {
  it("deletes on confirm", async () => {
    api.del.mockResolvedValue({ isSuccess: true });
    const onOpenChange = vi.fn();
    wrap(
      <DeleteWorkflow workflowId="w1" open onOpenChange={onOpenChange} />,
    );
    fireEvent.click(screen.getByText("Yes"));
    await waitFor(() => expect(api.del).toHaveBeenCalledWith({ id: "w1" }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("shows an error toast on failure", async () => {
    api.del.mockResolvedValue({ isSuccess: false, errors: "x" });
    wrap(<DeleteWorkflow workflowId="w1" open onOpenChange={vi.fn()} />);
    fireEvent.click(screen.getByText("Yes"));
    await waitFor(() => expect(toasts.showErrorToast).toHaveBeenCalled());
  });
});

describe("DuplicateWorkflow", () => {
  it("duplicates a workflow and navigates", async () => {
    api.duplicate.mockResolvedValue({ isSuccess: true, itemId: "w2" });
    wrap(
      <DuplicateWorkflow
        open
        onOpenChange={vi.fn()}
        workflowId="w1"
        name="Flow"
      />,
    );
    fireEvent.click(screen.getByText("Confirm"));
    await waitFor(() =>
      expect(api.duplicate).toHaveBeenCalledWith(
        expect.objectContaining({ workflowId: "w1" }),
      ),
    );
    expect(navigate).toHaveBeenCalledWith("workflow/w2");
  });
});
