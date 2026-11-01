import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { scheduleService } from "../../../services/schedule.service";
import { execution, queueSnapshot } from "../../../__mocks__/schedule-execution.mock";

vi.mock("@monaco-editor/react", () => ({
  default: ({ value, language }: { value?: string; language?: string }) => (
    <textarea data-testid="viewer" data-language={language} readOnly value={value ?? ""} />
  ),
}));
const toasts = vi.hoisted(() => ({ showSuccessToast: vi.fn(), showErrorToast: vi.fn() }));
vi.mock("@/hooks/use-toast", () => toasts);

import { ScheduleExecutionSheet } from "./schedule-execution-sheet";

const getExecution = () => vi.spyOn(scheduleService, "getScheduleExecution");

const open = (executionId: string | null = "e1", onClose = vi.fn()) =>
  renderWithProviders(
    <ScheduleExecutionSheet scheduleId="s1" executionId={executionId} tz="UTC" currentName="Nightly sync" onClose={onClose} />,
  );

describe("ScheduleExecutionSheet", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.clearAllMocks();
    Object.defineProperty(navigator, "clipboard", { value: { writeText: vi.fn().mockResolvedValue(undefined) }, configurable: true });
  });

  it("renders nothing and fetches nothing while closed", () => {
    const spy = getExecution();
    open(null);
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(spy).not.toHaveBeenCalled();
  });

  it("fetches once and shows the five sections for a webhook run", async () => {
    const spy = getExecution().mockResolvedValue({ isSuccess: true, data: execution() });
    open();

    const dialog = await screen.findByRole("dialog");
    await within(dialog).findByRole("region", { name: "Overview" });
    for (const name of ["Response", "Request sent", "Schedule at fire time", "Raw snapshot"]) {
      expect(within(dialog).getByRole("region", { name })).toBeTruthy();
    }
    expect(within(dialog).getByText("https://hooks.example.com/x?k=1")).toBeTruthy();
    expect(within(dialog).getByText("Signed (x-signature-sha256)")).toBeTruthy();
    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy).toHaveBeenCalledWith({ scheduleId: "s1", executionId: "e1" });
  });

  it("labels an accepted queue send as Queued", async () => {
    getExecution().mockResolvedValue({ isSuccess: true, data: execution({ status: 202, snapshot: queueSnapshot, body: "Sent to queue orders-queue." }) });
    open();
    expect((await screen.findAllByText("Queued")).length).toBeGreaterThan(0);
    expect(screen.getByText("orders-queue")).toBeTruthy();
  });

  it("replaces the snapshot sections when there is no snapshot", async () => {
    getExecution().mockResolvedValue({ isSuccess: true, data: execution({ snapshot: null, body: null }) });
    open();
    expect(await screen.findByText("Schedule snapshot unavailable for this run.")).toBeTruthy();
    expect(screen.getByText("No body.")).toBeTruthy();
    expect(screen.queryByRole("region", { name: "Request sent" })).toBeNull();
    expect(screen.queryByRole("region", { name: "Raw snapshot" })).toBeNull();
  });

  it("shows an invalid snapshot as raw text, expanded", async () => {
    getExecution().mockResolvedValue({ isSuccess: true, data: execution({ snapshot: "{oops", body: null }) });
    open();
    expect(await screen.findByText("Schedule snapshot unavailable for this run.")).toBeTruthy();
    expect(screen.getByRole("region", { name: "Raw snapshot" })).toBeTruthy();
    expect(screen.getByDisplayValue("{oops")).toBeTruthy();
    expect(screen.queryByRole("region", { name: "Request sent" })).toBeNull();
  });

  it("shows the error state with Retry, which refetches", async () => {
    const spy = getExecution().mockResolvedValue({ isSuccess: false, data: null, errors: { Message: "Something went wrong" } });
    open();
    await userEvent.click(await screen.findByRole("button", { name: "Retry" }));
    await waitFor(() => expect(spy).toHaveBeenCalledTimes(2));
    expect(screen.getByText("Couldn't load this execution.")).toBeTruthy();
  });

  it("shows not-found for an execution that no longer exists", async () => {
    getExecution().mockResolvedValue({ isSuccess: false, data: null, errors: { not_found: "Execution not found" } });
    open();
    expect(await screen.findByText("This execution no longer exists.")).toBeTruthy();
  });

  it("shows the error state when the request fails", async () => {
    getExecution().mockRejectedValue(new Error("network"));
    open();
    expect(await screen.findByText("Couldn't load this execution.")).toBeTruthy();
  });

  it("shows a loading state first", async () => {
    getExecution().mockReturnValue(new Promise(() => undefined));
    open();
    expect(await screen.findByText("Loading execution…")).toBeTruthy();
  });

  it("closes on Escape", async () => {
    getExecution().mockResolvedValue({ isSuccess: true, data: execution() });
    const onClose = vi.fn();
    open("e1", onClose);
    await screen.findByRole("region", { name: "Overview" });
    await userEvent.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalled();
  });

  it("copies the body with a toast", async () => {
    getExecution().mockResolvedValue({ isSuccess: true, data: execution() });
    open();
    await userEvent.click(await screen.findByRole("button", { name: "Copy response body" }));
    await waitFor(() => expect(toasts.showSuccessToast).toHaveBeenCalled());
  });
});
