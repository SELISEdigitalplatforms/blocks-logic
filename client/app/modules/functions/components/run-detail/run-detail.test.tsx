import { describe, expect, it, vi, beforeEach } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { RunDetail } from "./run-detail";

const getRun = vi.fn();
const getRunLogs = vi.fn();

vi.mock("../../services/function.service", () => ({
  functionService: {
    getRun: (...args: unknown[]) => getRun(...args),
    getRunLogs: (...args: unknown[]) => getRunLogs(...args),
    replayRun: vi.fn(),
    cancelRun: vi.fn(),
  },
}));

const RAW_MESSAGE =
  "the function module failed to load: Cannot find package 'ky' imported from /function/index.js";

const run = (overrides: Record<string, unknown> = {}) => ({
  id: "run_1",
  functionId: "fn_1",
  status: "Failed",
  versionNumber: 0,
  attempts: [],
  attempt: 1,
  maxAttempts: 1,
  createdDate: "2026-09-13T09:44:38.136Z",
  input: "{}",
  outputResults: [],
  logsTruncated: false,
  ...overrides,
});

describe("RunDetail", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getRunLogs.mockResolvedValue({ data: [], totalCount: 0 });
  });

  it("shows the runner's own message, not just the friendly summary of its code", async () => {
    // This is the whole complaint: with a known error code the portal showed the generic
    // sentence and the actual message — which package, which file — was readable only by
    // opening the network tab.
    getRun.mockResolvedValue(run({ errorCode: "UserRuntimeError", errorMessage: RAW_MESSAGE }));

    renderWithProviders(<RunDetail runId="run_1" />);

    await waitFor(() => expect(screen.getByText(RAW_MESSAGE)).toBeTruthy());
    expect(screen.getByText("UserRuntimeError")).toBeTruthy();
    // The hint reads the message, not just the code: this run died while importing the module,
    // so "the handler threw, the stack is in the logs" would point at logs that cannot exist.
    expect(screen.getByText(/Add the package to package\.json/)).toBeTruthy();
    expect(screen.queryByText(/The handler threw/)).toBeNull();
  });

  it("names the handler's scope when ctx was used at module level", async () => {
    getRun.mockResolvedValue(
      run({
        errorCode: "UserRuntimeError",
        errorMessage: "the function module failed to load: ctx is not defined",
      }),
    );

    renderWithProviders(<RunDetail runId="run_1" />);

    await waitFor(() => expect(screen.getByText(/parameters of your handler/)).toBeTruthy());
  });

  it("still explains a code that arrives with no message", async () => {
    getRun.mockResolvedValue(run({ errorCode: "TimedOut", errorMessage: null }));

    renderWithProviders(<RunDetail runId="run_1" />);

    await waitFor(() => expect(screen.getByText(/passed its timeout/)).toBeTruthy());
  });

  it("shows a message that arrives with no code at all", async () => {
    getRun.mockResolvedValue(run({ errorCode: null, errorMessage: "something unmapped broke" }));

    renderWithProviders(<RunDetail runId="run_1" />);

    await waitFor(() => expect(screen.getByText("something unmapped broke")).toBeTruthy());
  });

  it("keeps the structured payload a developer logged", async () => {
    // ctx.log("x", { orderId: 7 }) — the second argument was being dropped on the floor.
    getRun.mockResolvedValue(run({ status: "Succeeded" }));
    getRunLogs.mockResolvedValue({
      data: [
        {
          seq: 1,
          timestamp: "2026-09-13T09:44:39.000Z",
          level: "info",
          message: "reconciling",
          data: '{"orderId":7}',
        },
      ],
      totalCount: 1,
    });

    renderWithProviders(<RunDetail runId="run_1" />);

    await waitFor(() => expect(screen.getByText("reconciling")).toBeTruthy());
    expect(screen.getByText('{"orderId":7}')).toBeTruthy();
  });

  it("says nothing about errors when the run succeeded", async () => {
    getRun.mockResolvedValue(run({ status: "Succeeded", errorCode: null, errorMessage: null }));

    renderWithProviders(<RunDetail runId="run_1" />);

    await waitFor(() => expect(screen.getByText("No logs recorded.")).toBeTruthy());
    expect(screen.queryByText(/UserRuntimeError/)).toBeNull();
  });
});
