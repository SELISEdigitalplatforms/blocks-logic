import { fireEvent, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { OutputPanel } from "./output-panel";

const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
  showInfoToast: vi.fn(),
}));
vi.mock("@/hooks/use-toast", () => toasts);

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const node = (extra: Record<string, unknown> = {}): any => ({
  id: "n1",
  name: "n1",
  type: "action",
  position: { x: 0, y: 0 },
  parameters: {},
  ...extra,
});

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const seedSelected = (extra: Record<string, unknown> = {}) => (store: any) => {
  const n = node(extra);
  store.getState().addNode(n);
  store.setState({ selectedNode: n, editorMode: "editor" });
};

beforeEach(() => vi.clearAllMocks());

describe("OutputPanel", () => {
  it("renders nothing without a selected node", () => {
    const { container } = renderWithProviders(<OutputPanel />);
    expect(container.textContent).toBe("");
  });

  it("shows the schema derived from pinned data and copies a value", () => {
    renderWithProviders(<OutputPanel />, {
      seedWorkflow: seedSelected({ pinData: [{ name: "Ada", age: 30 }] }),
    });
    expect(screen.getByText("Output")).toBeTruthy();
    expect(screen.getByText("name:")).toBeTruthy();
    // clicking a schema field copies the value (no throw)
    fireEvent.click(screen.getByText("name:"));
  });

  it("shows the no-schema message when there is no output", () => {
    renderWithProviders(<OutputPanel />, { seedWorkflow: seedSelected() });
    expect(
      screen.getByText("No runtime output schema available."),
    ).toBeTruthy();
  });

  it("supports the mock-data editing flow with invalid JSON", () => {
    const { container } = renderWithProviders(<OutputPanel />, {
      seedWorkflow: seedSelected(),
    });
    fireEvent.click(screen.getByText("Set mock data"));
    const textarea = container.querySelector("textarea") as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: "not json" } });
    fireEvent.click(screen.getByText("Save"));
    expect(toasts.showErrorToast).toHaveBeenCalledWith({
      errors: "Invalid JSON format",
    });
  });

  it("saves valid mock data as pinData", () => {
    const { container } = renderWithProviders(<OutputPanel />, {
      seedWorkflow: seedSelected(),
    });
    fireEvent.click(screen.getByText("Set mock data"));
    const textarea = container.querySelector("textarea") as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: '{"x":1}' } });
    fireEvent.click(screen.getByText("Save"));
    // editing panel closes -> tabs come back
    expect(screen.getByText("Schema")).toBeTruthy();
  });

  it("cancels mock data editing", () => {
    renderWithProviders(<OutputPanel />, { seedWorkflow: seedSelected() });
    fireEvent.click(screen.getByText("Set mock data"));
    fireEvent.click(screen.getByText("Cancel"));
    expect(screen.getByText("Set mock data")).toBeTruthy();
  });

  it("toggles collapse via the chevron button", () => {
    const onToggleCollapse = vi.fn();
    renderWithProviders(
      <OutputPanel isCollapsed={false} onToggleCollapse={onToggleCollapse} />,
      { seedWorkflow: seedSelected({ pinData: [{ a: 1 }] }) },
    );
    // the collapse button is the last button in the header
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[buttons.length - 1]);
    expect(onToggleCollapse).toHaveBeenCalled();
  });

  it("switches to the table and json tabs", async () => {
    const user = userEvent.setup();
    renderWithProviders(<OutputPanel />, {
      seedWorkflow: seedSelected({ pinData: [{ name: "Ada" }] }),
    });
    await user.click(screen.getByRole("tab", { name: "Table" }));
    await waitFor(() => expect(screen.getByText("name")).toBeTruthy());
    await user.click(screen.getByRole("tab", { name: "JSON" }));
    await waitFor(() =>
      expect(document.querySelector("pre code")).toBeTruthy(),
    );
  });

  // Seeds a node plus runtime executed items (not pinData) grouped by branch,
  // in run mode so the branch-grouping code path is exercised.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const seedRuntimeBranches = (store: any) => {
    const n = node();
    store.getState().addNode(n);
    store.setState({
      selectedNode: n,
      editorMode: "run",
      executedItems: [
        { nodeId: "n1", itemIndex: 0, branch: "True", data: { Output: { city: "Paris" } } },
        { nodeId: "n1", itemIndex: 1, branch: "False", data: { Output: "plain-value" } },
        // items for another node are ignored
        { nodeId: "other", itemIndex: 0, branch: "True", data: { Output: { city: "X" } } },
      ],
    });
  };

  it("groups runtime executed items by branch in the schema tab", () => {
    renderWithProviders(<OutputPanel />, { seedWorkflow: seedRuntimeBranches });
    expect(screen.getByText("Branch: True")).toBeTruthy();
    expect(screen.getByText("Branch: False")).toBeTruthy();
    // object row exposes its field key, primitive row is rendered directly
    expect(screen.getByText("city:")).toBeTruthy();
    expect(screen.getByText("plain-value")).toBeTruthy();
  });

  it("renders the multi-branch table with a primitive value column", async () => {
    const user = userEvent.setup();
    renderWithProviders(<OutputPanel />, { seedWorkflow: seedRuntimeBranches });
    await user.click(screen.getByRole("tab", { name: "Table" }));
    await waitFor(() => expect(screen.getAllByText(/Branch:/).length).toBeGreaterThan(0));
    // the False branch holds a primitive, forcing the "(value)" column header
    expect(screen.getByText("(value)")).toBeTruthy();
    expect(screen.getByText("city")).toBeTruthy();
  });

  it("renders the multi-branch json tab", async () => {
    const user = userEvent.setup();
    renderWithProviders(<OutputPanel />, { seedWorkflow: seedRuntimeBranches });
    await user.click(screen.getByRole("tab", { name: "JSON" }));
    await waitFor(() =>
      expect(document.querySelectorAll("pre code").length).toBe(2),
    );
  });

  it("hides the schema body while collapsed", () => {
    renderWithProviders(<OutputPanel isCollapsed onToggleCollapse={() => {}} />, {
      seedWorkflow: seedSelected({ pinData: [{ name: "Ada" }] }),
    });
    // header still shows, but the schema field is not rendered when collapsed
    expect(screen.getByText("Output")).toBeTruthy();
    expect(screen.queryByText("name:")).toBeNull();
  });
});
