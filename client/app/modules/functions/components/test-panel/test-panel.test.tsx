import { describe, expect, it, vi, beforeEach } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { TestPanel } from "./test-panel";
import { useFunctionEditorStore } from "../../store/function-editor-store";

const testFunction = vi.fn();
const getBuild = vi.fn();
const getRun = vi.fn();
const getRunLogs = vi.fn();

vi.mock("../../services/function.service", () => ({
  functionService: {
    testFunction: (...args: unknown[]) => testFunction(...args),
    getBuild: (...args: unknown[]) => getBuild(...args),
    getRun: (...args: unknown[]) => getRun(...args),
    getRunLogs: (...args: unknown[]) => getRunLogs(...args),
  },
}));

const renderPanel = () => renderWithProviders(<TestPanel functionId="fn_1" onOpenRun={vi.fn()} />);

describe("TestPanel", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useFunctionEditorStore.setState({ testInput: "{}" });
    getRun.mockResolvedValue(null);
    getRunLogs.mockResolvedValue({ data: [], totalCount: 0 });
  });

  it("shows the design's five stages, Building included", async () => {
    testFunction.mockResolvedValue({ runId: "", status: "", buildId: "build_1" });
    getBuild.mockResolvedValue({ itemId: "build_1", status: "Building" });

    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /run test/i }));

    await waitFor(() => expect(screen.getByText("Building")).toBeTruthy());
    ["Saving", "Building", "Queued", "Running", "Done"].forEach((stage) => {
      expect(screen.getByText(stage)).toBeTruthy();
    });
  });

  it("follows the build when Test answers with one instead of a run", async () => {
    testFunction.mockResolvedValue({ runId: "", status: "", buildId: "build_1" });
    getBuild.mockResolvedValue({ itemId: "build_1", status: "Building" });

    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /run test/i }));

    // The build's own progress, rather than the 300 s wait that used to end in a 500.
    await waitFor(() => expect(getBuild).toHaveBeenCalledWith("build_1"));
    await waitFor(() => expect(screen.getByText(/building image/i)).toBeTruthy());
  });

  it("does not run when the pre-run save failed", async () => {
    testFunction.mockResolvedValue({ runId: "run_1", status: "Queued" });
    const onBeforeRun = vi.fn().mockResolvedValue(false);

    renderWithProviders(
      <TestPanel functionId="fn_1" onOpenRun={vi.fn()} onBeforeRun={onBeforeRun} />,
    );
    await userEvent.click(screen.getByRole("button", { name: /run test/i }));

    // Testing the previous server-side source after a rejected save is worse than not testing.
    expect(onBeforeRun).toHaveBeenCalled();
    expect(testFunction).not.toHaveBeenCalled();
  });

  it("runs the test again by itself once the build succeeds", async () => {
    testFunction
      .mockResolvedValueOnce({ runId: "", status: "", buildId: "build_1" })
      .mockResolvedValueOnce({ runId: "run_1", status: "Queued" });
    getBuild.mockResolvedValue({ itemId: "build_1", status: "Succeeded" });

    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /run test/i }));

    await waitFor(() => expect(testFunction).toHaveBeenCalledTimes(2));
  });
});
