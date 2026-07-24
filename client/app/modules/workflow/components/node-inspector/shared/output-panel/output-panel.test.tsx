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
});
