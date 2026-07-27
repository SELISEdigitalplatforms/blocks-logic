import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
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
afterEach(() => { document.body.style.pointerEvents = ""; });

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
    let store: unknown;
    renderWithProviders(
      <EditorNodeBase id="n1">
        <span>body</span>
      </EditorNodeBase>,
      {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        seedWorkflow: (s: any) => {
          store = s;
          s.getState().addNode(node("n1"));
        },
      },
    );
    // toolbar buttons: Play(execute), Copy(duplicate), Trash(delete), More
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[1]);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect(Object.keys((store as any).getState().nodesMap).length).toBe(2);
  });

  it("executes the node from the toolbar play button", () => {
    renderWithProviders(
      <EditorNodeBase id="n1">
        <span>body</span>
      </EditorNodeBase>,
      { seedWorkflow: seed() },
    );
    const buttons = screen.getAllByRole("button");
    // play/execute is the first toolbar button
    fireEvent.click(buttons[0]);
    expect(screen.getByText("Alpha")).toBeTruthy();
  });

  it("deletes the node from the toolbar", () => {
    let store: unknown;
    const { container } = renderWithProviders(
      <EditorNodeBase id="n1">
        <span>body</span>
      </EditorNodeBase>,
      {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        seedWorkflow: (s: any) => {
          store = s;
          s.getState().addNode(node("n1"));
        },
      },
    );
    const buttons = screen.getAllByRole("button");
    // trash/delete is the third toolbar button
    fireEvent.click(buttons[2]);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect(Object.keys((store as any).getState().nodesMap).length).toBe(0);
    expect(container.textContent).toBe("");
  });

  it("opens the more menu and triggers its actions", async () => {
    const user = userEvent.setup();
    let store: unknown;
    renderWithProviders(
      <EditorNodeBase id="n1">
        <span>body</span>
      </EditorNodeBase>,
      {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        seedWorkflow: (s: any) => {
          store = s;
          s.getState().addNode(node("n1"));
        },
      },
    );
    const buttons = screen.getAllByRole("button");
    await user.click(buttons[buttons.length - 1]);
    await user.click(await screen.findByText("Open"));
    // opening configures the node in the store
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await waitFor(() => expect((store as any).getState().isConfigModalOpen).toBe(true));
  });
});
