import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { localDate } from "../../utils/execution-date-range";

type Props = {
  values: Record<string, unknown>;
  defaultValues: Record<string, unknown>;
  filters: { key: string; type: string; label: string }[];
  onChange: (key: string, value: unknown) => void;
  onReset: () => void;
};

// Capture what the toolbar hands the shared FilterToolbar, and drive its callbacks directly.
const captured = vi.hoisted(() => ({ props: null as Props | null }));
vi.mock("@/components/filter-toolbar", () => ({
  FilterToolbar: (props: Props) => {
    captured.props = props;
    return <div data-testid="toolbar" />;
  },
}));

const query = vi.hoisted(() => ({
  params: { outcome: "all", from: "", to: "", page: 3, pageSize: 20 },
  set: vi.fn(),
}));
vi.mock("../../hooks/use-schedule-execution-query-params", () => ({
  useScheduleExecutionQueryParams: () => ({ queryParams: query.params, setQueryParams: query.set }),
}));

import { ScheduleExecutionFilterToolbar } from "./schedule-execution-filter-toolbar";

const applyUpdater = () => {
  const updater = query.set.mock.calls.at(-1)?.[0] as (p: typeof query.params) => unknown;
  return updater(query.params);
};

describe("ScheduleExecutionFilterToolbar", () => {
  beforeEach(() => {
    query.set.mockClear();
    query.params = { outcome: "all", from: "", to: "", page: 3, pageSize: 20 };
  });

  it("offers the outcome and fired-date filters, in that order", () => {
    render(<ScheduleExecutionFilterToolbar />);
    expect(screen.getByTestId("toolbar")).toBeTruthy();
    expect(captured.props?.filters.map((f) => [f.key, f.type, f.label])).toEqual([
      ["outcome", "Radio", "Outcome"],
      ["firedDate", "DateRange", "Fired date"],
    ]);
    expect(captured.props?.defaultValues).toEqual({ outcome: "all", firedDate: null });
    expect(captured.props?.values).toEqual({ outcome: "all", firedDate: null });
  });

  it("maps URL days to the date control's value", () => {
    query.params = { ...query.params, from: "2026-10-20", to: "2026-10-26" };
    render(<ScheduleExecutionFilterToolbar />);
    expect(captured.props?.values.firedDate).toEqual({ from: localDate("2026-10-20"), to: localDate("2026-10-26") });
  });

  it("sets the outcome and resets to page 0", () => {
    render(<ScheduleExecutionFilterToolbar />);
    captured.props?.onChange("outcome", "Failed");
    expect(applyUpdater()).toEqual({ ...query.params, outcome: "Failed", page: 0 });
  });

  it("maps an applied date range to URL days and resets to page 0", () => {
    render(<ScheduleExecutionFilterToolbar />);
    captured.props?.onChange("firedDate", { from: localDate("2026-10-26") });
    expect(applyUpdater()).toEqual({ ...query.params, from: "2026-10-26", to: "", page: 0 });

    captured.props?.onChange("firedDate", null);
    expect(applyUpdater()).toEqual({ ...query.params, from: "", to: "", page: 0 });
  });

  it("clears every param on reset", () => {
    render(<ScheduleExecutionFilterToolbar />);
    captured.props?.onReset();
    expect(query.set).toHaveBeenCalledWith(null);
  });
});
