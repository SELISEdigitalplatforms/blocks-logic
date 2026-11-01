import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { NuqsTestingAdapter, type UrlUpdateEvent } from "nuqs/adapters/testing";
import { MemoryRouter } from "react-router";
import { TooltipProvider } from "@/components/ui-kits/tooltip/tooltip";
import { scheduleService } from "../../services/schedule.service";
import { execution, listItem, schedule } from "../../__mocks__/schedule-execution.mock";
import { ISchedule } from "../../types/schedule.service.type";

vi.mock("@monaco-editor/react", () => ({
  default: ({ value }: { value?: string }) => <textarea data-testid="viewer" readOnly value={value ?? ""} />,
}));

// Pin the viewer's zone so the "Dates are in {tz}" note does not depend on the machine running the tests.
vi.mock("../../utils/timezone", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../utils/timezone")>()),
  browserTimeZone: () => "UTC",
}));

import { listStateOf, ScheduleExecutionsTab } from "./schedule-executions-tab";

const onUrlUpdate = vi.fn<(event: UrlUpdateEvent) => void>();

const renderTab = ({ search = "", active = true, value = schedule() }: { search?: string; active?: boolean; value?: ISchedule } = {}) => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  return render(
    <QueryClientProvider client={client}>
      <NuqsTestingAdapter searchParams={search} onUrlUpdate={onUrlUpdate} hasMemory>
        <MemoryRouter>
          <TooltipProvider>
            <ScheduleExecutionsTab schedule={value} active={active} />
          </TooltipProvider>
        </MemoryRouter>
      </NuqsTestingAdapter>
    </QueryClientProvider>,
  );
};

const page = (count: number, totalCount: number) => ({
  isSuccess: true,
  totalCount,
  data: Array.from({ length: count }, (_, i) => listItem({ id: `e${i + 1}` })),
});

const listSpy = () => vi.spyOn(scheduleService, "getScheduleExecutions");

describe("ScheduleExecutionsTab", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    onUrlUpdate.mockClear();
  });

  it("issues no request while the tab is inactive", () => {
    const spy = listSpy();
    renderTab({ active: false });
    expect(spy).not.toHaveBeenCalled();
  });

  it("loads the first page with one request and shows rows, toolbar and pagination", async () => {
    const spy = listSpy().mockResolvedValue(page(10, 25));
    renderTab();

    expect(await screen.findAllByText("HTTP 200")).toHaveLength(10);
    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy).toHaveBeenCalledWith({ scheduleId: "s1", outcome: null, startDate: null, endDate: null, pageNumber: 0, pageSize: 10 });
    expect(screen.getByText("Page 1 of 3")).toBeTruthy();
    expect(screen.getAllByText("Outcome").length).toBeGreaterThan(0);
  });

  it("requests the page, outcome and dates in the URL", async () => {
    const spy = listSpy().mockResolvedValue(page(1, 101));
    renderTab({ search: "?outcome=Failed&from=2026-10-26&page=2&pageSize=50", value: schedule({ timeZone: "Asia/Dhaka" }) });

    await screen.findByText("HTTP 200");
    expect(spy).toHaveBeenCalledWith({
      scheduleId: "s1",
      outcome: "Failed",
      startDate: "2026-10-25T18:00:00.000Z",
      endDate: "2026-10-26T18:00:00.000Z",
      pageNumber: 2,
      pageSize: 50,
    });
    expect(screen.getByText("Dates are in Asia/Dhaka")).toBeTruthy();
  });

  it("hides the zone note when the schedule's zone is the viewer's", async () => {
    listSpy().mockResolvedValue(page(1, 1));
    renderTab({ search: "?from=2026-10-26" });
    await screen.findByText("HTTP 200");
    expect(screen.queryByText(/Dates are in/)).toBeNull();
  });

  it("ignores a malformed date range in the URL", async () => {
    const spy = listSpy().mockResolvedValue(page(1, 1));
    renderTab({ search: "?from=2026-10-26&to=2026-10-01" });
    await screen.findByText("HTTP 200");
    expect(spy).toHaveBeenCalledWith(expect.objectContaining({ startDate: null, endDate: null }));
  });

  it("moves to the next page through the URL", async () => {
    listSpy().mockResolvedValue(page(10, 25));
    const { container } = renderTab();
    await screen.findByText("Page 1 of 3");

    const next = container.querySelector(".lucide-chevron-right")?.closest("button");
    if (!next) throw new Error("next button missing");
    await userEvent.click(next);

    await waitFor(() => expect(onUrlUpdate).toHaveBeenCalled());
    expect(onUrlUpdate.mock.calls.at(-1)?.[0].queryString).toBe("?page=1");
  });

  it("shows the empty state without the toolbar when nothing has run", async () => {
    listSpy().mockResolvedValue(page(0, 0));
    renderTab();
    expect(await screen.findByText("No executions yet")).toBeTruthy();
    expect(screen.getByText("Runs will appear here after the schedule fires.")).toBeTruthy();
    expect(screen.queryByText("Outcome")).toBeNull();
  });

  it("shows the no-match state with the toolbar when a filter is set", async () => {
    listSpy().mockResolvedValue(page(0, 0));
    renderTab({ search: "?outcome=Succeeded" });
    expect(await screen.findByText("No executions match this filter.")).toBeTruthy();
    expect(screen.getAllByText("Outcome").length).toBeGreaterThan(0);
  });

  it("shows the list error with Retry for a failed envelope or request", async () => {
    const spy = listSpy().mockResolvedValue({ isSuccess: false, data: null, totalCount: 0, errors: { Message: "Something went wrong" } });
    renderTab();
    await userEvent.click(await screen.findByRole("button", { name: "Retry" }));
    await waitFor(() => expect(spy).toHaveBeenCalledTimes(2));
    expect(screen.queryByText(/Coming Soon/)).toBeNull();
  });

  it("shows the list error when the request throws", async () => {
    listSpy().mockRejectedValue(new Error("network"));
    renderTab();
    expect(await screen.findByText("Couldn't load executions")).toBeTruthy();
  });

  it("shows the schedule-gone state on not_found", async () => {
    listSpy().mockResolvedValue({ isSuccess: false, data: null, totalCount: 0, errors: { not_found: "Schedule not found" } });
    renderTab();
    expect(await screen.findByText("This schedule no longer exists.")).toBeTruthy();
  });

  it("opens the sheet on a row without touching the URL", async () => {
    listSpy().mockResolvedValue(page(1, 1));
    const detail = vi.spyOn(scheduleService, "getScheduleExecution").mockResolvedValue({ isSuccess: true, data: execution() });
    renderTab();

    await userEvent.click(await screen.findByRole("button", { name: /Open execution fired at/ }));

    expect(await screen.findByRole("dialog")).toBeTruthy();
    expect(detail).toHaveBeenCalledWith({ scheduleId: "s1", executionId: "e1" });
    expect(onUrlUpdate).not.toHaveBeenCalled();

    await userEvent.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });
});

describe("listStateOf", () => {
  it("covers every state", () => {
    expect(listStateOf(undefined, false, true, false)).toBe("list");
    expect(listStateOf({ isSuccess: false, data: null, totalCount: 0, errors: { not_found: "x" } }, false, false, false)).toBe("gone");
    expect(listStateOf(undefined, true, false, false)).toBe("error");
    expect(listStateOf({ isSuccess: true, data: [], totalCount: 0 }, false, false, false)).toBe("empty");
    expect(listStateOf({ isSuccess: true, data: [], totalCount: 0 }, false, false, true)).toBe("no-match");
    expect(listStateOf({ isSuccess: true, data: [], totalCount: 5 }, false, false, false)).toBe("list");
    expect(listStateOf(undefined, false, false, false)).toBe("list");
  });
});
