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
const getRuns = vi.fn();
const cancelRun = vi.fn();

vi.mock("../../services/function.service", () => ({
  functionService: {
    testFunction: (...args: unknown[]) => testFunction(...args),
    getBuild: (...args: unknown[]) => getBuild(...args),
    getRun: (...args: unknown[]) => getRun(...args),
    getRunLogs: (...args: unknown[]) => getRunLogs(...args),
    getRuns: (...args: unknown[]) => getRuns(...args),
    cancelRun: (...args: unknown[]) => cancelRun(...args),
  },
}));

const renderPanel = () => renderWithProviders(<TestPanel functionId="fn_1" onOpenRun={vi.fn()} />);

/** What the genesis HttpClient throws for a non-2xx answer. */
const httpError = (status: number, errors: Record<string, string>) =>
  Object.assign(new Error(JSON.stringify(errors)), { status, errors });

describe("TestPanel", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useFunctionEditorStore.setState({ testInput: "{}" });
    getRun.mockResolvedValue(null);
    getRunLogs.mockResolvedValue({ data: [], totalCount: 0 });
    getRuns.mockResolvedValue({ data: [], totalCount: 0 });
  });

  it("shows each real step, with the current one explained", async () => {
    testFunction.mockReturnValue(new Promise(() => {}));

    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /run test/i }));

    const steps = await screen.findByRole("list", { name: "Test progress" });
    ["Save code", "Wait for a runner", "Build and start sandbox", "Run handler", "Result"].forEach(
      (step) => expect(steps.textContent).toContain(step),
    );
    expect(screen.getByText(/queued\. a runner picks it up/i)).toBeTruthy();
  });

  it("picks up the live run while the Test request is still open", async () => {
    // The request answers only when the run ends; the panel must not sit on "Starting…".
    testFunction.mockReturnValue(new Promise(() => {}));
    getRuns
      .mockResolvedValueOnce({
        data: [{ id: "old_run", status: "Succeeded", createdDate: new Date().toISOString() }],
      })
      .mockResolvedValueOnce({
        // the re-read just before the request is sent: still the old run
        data: [{ id: "old_run", status: "Succeeded", createdDate: new Date().toISOString() }],
      })
      .mockResolvedValue({
        data: [{ id: "run_new", status: "Running", createdDate: new Date().toISOString() }],
      });
    getRun.mockImplementation((id: string) =>
      Promise.resolve(id === "run_new" ? { id, status: "Running", attempts: [] } : null),
    );
    getRunLogs.mockResolvedValue({
      data: [{ seq: 1, level: "info", message: "auth-check", timestamp: "" }],
      totalCount: 1,
    });

    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /run test/i }));

    await waitFor(() => expect(getRun).toHaveBeenCalledWith("run_new"), { timeout: 4000 });
    expect(getRun).not.toHaveBeenCalledWith("old_run");
    await waitFor(() => expect(screen.getByText("auth-check")).toBeTruthy());
    expect(screen.getByText(/your handler is running/i)).toBeTruthy();
  });

  it("shows the build step while the runner builds, not a stuck queue", async () => {
    testFunction.mockResolvedValue({ runId: "run_1", status: "Queued" });
    // The Api reports a test building on a runner as Claimed.
    getRun.mockResolvedValue({ id: "run_1", status: "Claimed", attempts: [] });

    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /run test/i }));

    await waitFor(() => {
      const current = document.querySelector('[aria-current="step"]');
      expect(current?.textContent).toContain("Build and start sandbox");
    });
  });

  it("shows a 429 in the panel and counts down instead of a generic toast", async () => {
    testFunction.mockRejectedValue(
      httpError(429, {
        code: "FUNCTION_RATE_LIMITED",
        message: "this function was tested less than 120 seconds ago; try again in 87s",
      }),
    );

    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /run test/i }));

    expect((await screen.findByRole("alert")).textContent).toMatch(/try again in 87s/);
    const button = screen.getByRole("button", { name: /next test in 8\ds/i });
    expect(button.hasAttribute("disabled")).toBe(true);
  });

  it("never sends a second Test while one is open (header button included)", async () => {
    testFunction.mockReturnValue(new Promise(() => {}));

    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /run test/i }));
    await waitFor(() => expect(testFunction).toHaveBeenCalledTimes(1));

    useFunctionEditorStore.setState({ testRunRequestedAt: Date.now() });
    await userEvent.keyboard("{Control>}{Enter}{/Control}");

    expect(testFunction).toHaveBeenCalledTimes(1);
  });

  it("shows what the handler returned", async () => {
    testFunction.mockResolvedValue({ runId: "run_1", status: "Succeeded" });
    getRun.mockResolvedValue({
      id: "run_1",
      status: "Succeeded",
      result: '{"ok":true,"services":{"mongo":"healthy"}}',
      attempts: [],
    });

    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /run test/i }));

    const returned = await screen.findByLabelText("Returned value");
    expect(returned.textContent).toContain('"mongo": "healthy"');
    expect(screen.getByText("Succeeded", { selector: "li span" })).toBeTruthy();
  });

  it("follows the build when Test answers with one instead of a run", async () => {
    testFunction.mockResolvedValue({ runId: "", status: "", buildId: "build_1" });
    getBuild.mockResolvedValue({ itemId: "build_1", status: "Building" });

    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /run test/i }));

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

    expect(onBeforeRun).toHaveBeenCalled();
    expect(testFunction).not.toHaveBeenCalled();
  });

  it("shows the run's own error message, not only the code's friendly summary", async () => {
    const raw =
      "the function module failed to load: Cannot find package 'ky' imported from /function/index.js";
    testFunction.mockResolvedValue({ runId: "run_1", status: "Queued" });
    getRun.mockResolvedValue({
      id: "run_1",
      status: "Failed",
      errorCode: "UserRuntimeError",
      errorMessage: raw,
      attempts: [],
    });

    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /run test/i }));

    await waitFor(() => expect(screen.getByText(raw)).toBeTruthy());
    expect(screen.getByText("UserRuntimeError")).toBeTruthy();
  });

  it("follows a test's run and its own build together and never starts it twice", async () => {
    testFunction.mockResolvedValue({ runId: "run_1", status: "Queued", buildId: "build_1" });
    getBuild.mockResolvedValue({ itemId: "build_1", status: "Succeeded" });
    getRun.mockResolvedValue({ id: "run_1", status: "Succeeded", attempts: [] });

    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /run test/i }));

    await waitFor(() => expect(getBuild).toHaveBeenCalled());
    await waitFor(() => expect(getRun).toHaveBeenCalled());
    expect(testFunction).toHaveBeenCalledTimes(1);
  });

  it("offers no rebuild: every test already builds fresh", () => {
    renderPanel();

    expect(screen.queryByRole("button", { name: /rebuild image/i })).toBeNull();
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

  it("picks up a test still running after a reload", async () => {
    getRuns.mockResolvedValue({
      data: [{ id: "run_live", status: "Running", createdDate: new Date().toISOString() }],
    });
    getRun.mockResolvedValue({ id: "run_live", status: "Running", attempts: [] });

    renderPanel();

    await waitFor(() => expect(getRun).toHaveBeenCalledWith("run_live"));
    expect(await screen.findByRole("button", { name: /running/i })).toBeTruthy();
  });
});
