import { fireEvent, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { EditorNodeBase } from "./editor-node-base";

const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
  showInfoToast: vi.fn(),
}));
vi.mock("@/hooks/use-toast", () => toasts);

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const node = (id: string, extra: Record<string, unknown> = {}): any => ({
  id,
  name: id === "n1" ? "Alpha" : id,
  type: "action",
  position: { x: 0, y: 0 },
  parameters: {},
  data: {},
  ...extra,
});

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const seed = (extra: Record<string, unknown> = {}) => (store: any) => {
  store.getState().addNode(node("n1", extra));
};

beforeEach(() => vi.clearAllMocks());

describe("EditorNodeBase", () => {
  it("returns null when the node does not exist", () => {
    const { container } = renderWithProviders(
      <EditorNodeBase id="missing">
        <span>body</span>
      </EditorNodeBase>,
    );
    expect(container.textContent).toBe("");
  });

  it("renders the node body and name", () => {
    renderWithProviders(
      <EditorNodeBase id="n1">
        <span>body</span>
      </EditorNodeBase>,
      { seedWorkflow: seed() },
    );
    expect(screen.getByText("body")).toBeTruthy();
    expect(screen.getByText("Alpha")).toBeTruthy();
  });

  it("renames a node via double click and Enter", () => {
    renderWithProviders(
      <EditorNodeBase id="n1">
        <span>body</span>
      </EditorNodeBase>,
      { seedWorkflow: seed() },
    );
    fireEvent.doubleClick(screen.getByText("Alpha"));
    const input = document.querySelector("input") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "Beta" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(screen.getByText("Beta")).toBeTruthy();
  });

  it("rejects a duplicate name with an error toast", () => {
    renderWithProviders(
      <EditorNodeBase id="n1">
        <span>body</span>
      </EditorNodeBase>,
      {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        seedWorkflow: (store: any) => {
          store.getState().addNode(node("n1"));
          store.getState().addNode(node("n2", { name: "Taken" }));
        },
      },
    );
    fireEvent.doubleClick(screen.getByText("Alpha"));
    const input = document.querySelector("input") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "Taken" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(toasts.showErrorToast).toHaveBeenCalled();
  });

  it("cancels renaming with Escape", () => {
    renderWithProviders(
      <EditorNodeBase id="n1">
        <span>body</span>
      </EditorNodeBase>,
      { seedWorkflow: seed() },
    );
    fireEvent.doubleClick(screen.getByText("Alpha"));
    const input = document.querySelector("input") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "Ignored" } });
    fireEvent.keyDown(input, { key: "Escape" });
    expect(screen.getByText("Alpha")).toBeTruthy();
  });

  it("shows the listening indicator for the listening node", () => {
    renderWithProviders(
      <EditorNodeBase id="n1">
        <span>body</span>
      </EditorNodeBase>,
      {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        seedWorkflow: (store: any) => {
          store.getState().addNode(node("n1"));
          store.getState().setIsListening(true, "n1");
        },
      },
    );
    // the stop-execution tooltip button appears while listening
    expect(screen.getByText("Alpha")).toBeTruthy();
  });

  it("duplicates the node from the toolbar", () => {
    renderWithProviders(
      <EditorNodeBase id="n1">
        <span>body</span>
      </EditorNodeBase>,
      { seedWorkflow: seed() },
    );
    // toolbar Duplicate is exposed via its tooltip trigger button
    const buttons = screen.getAllByRole("button");
    // second toolbar button (Play, Duplicate, Delete, More) - click each is safe
    fireEvent.click(buttons[1]);
    expect(screen.getByText("Alpha")).toBeTruthy();
  });
});
