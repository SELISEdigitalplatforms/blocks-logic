import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const hook = vi.hoisted(() => ({ importWorkflow: vi.fn(), isImporting: false }));
vi.mock("@blocks-workflow/hooks/use-import-workflow", () => ({
  useImportWorkflow: () => hook,
}));

import { ImportWorkflow } from "./import-workflow";

beforeEach(() => {
  vi.clearAllMocks();
  hook.isImporting = false;
});

const fileInput = (container: HTMLElement) =>
  container.querySelector('input[type="file"]') as HTMLInputElement;

describe("ImportWorkflow", () => {
  it("renders a JSON-restricted file input and an Import button (H4)", () => {
    const { container } = render(<ImportWorkflow />);
    expect(screen.getByRole("button", { name: /import/i })).toBeTruthy();
    expect(fileInput(container).accept).toBe("application/json,.json");
  });

  it("passes the selected file to the import orchestration (H5)", () => {
    const { container } = render(<ImportWorkflow />);
    const file = new File(["{}"], "wf.json", { type: "application/json" });
    fireEvent.change(fileInput(container), { target: { files: [file] } });
    expect(hook.importWorkflow).toHaveBeenCalledWith(file);
  });

  it("does nothing when the picker is dismissed with no file", () => {
    const { container } = render(<ImportWorkflow />);
    fireEvent.change(fileInput(container), { target: { files: [] } });
    expect(hook.importWorkflow).not.toHaveBeenCalled();
  });

  it("disables the control while an import is in flight (C9)", () => {
    hook.isImporting = true;
    const { container } = render(<ImportWorkflow />);
    expect(screen.getByRole("button", { name: /import/i })).toHaveProperty("disabled", true);
    expect(fileInput(container).disabled).toBe(true);
  });
});
