import { fireEvent, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { NodeLibraryPanel } from "./node-library-panel";
import { NodeDefinitions } from "./node-definitions";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const openPanel = (store: any) => store.getState().openNodeLibraryPanel();

describe("NodeLibraryPanel", () => {
  it("renders the node options when the panel is open", () => {
    renderWithProviders(<NodeLibraryPanel />, { seedWorkflow: openPanel });
    expect(screen.getByText("Start your workflow")).toBeTruthy();
    expect(screen.getByText(NodeDefinitions[0].title)).toBeTruthy();
  });

  it("filters options by the search query", () => {
    renderWithProviders(<NodeLibraryPanel />, { seedWorkflow: openPanel });
    fireEvent.change(screen.getByPlaceholderText("Search"), {
      target: { value: "zzz-nonexistent" },
    });
    expect(screen.getByText(/No triggers found/)).toBeTruthy();
  });

  it("adds a node to the store when an option is selected", () => {
    let store: unknown;
    renderWithProviders(<NodeLibraryPanel />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (s: any) => {
        store = s;
        s.getState().openNodeLibraryPanel();
      },
    });
    const selectable = NodeDefinitions.find((n) => !n.isComingSoon)!;
    fireEvent.click(screen.getByText(selectable.title));
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const nodesMap = (store as any).getState().nodesMap;
    expect(Object.keys(nodesMap).length).toBe(1);
  });
});
