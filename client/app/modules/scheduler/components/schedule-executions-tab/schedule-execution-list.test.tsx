import { describe, expect, it, vi } from "vitest";
import { fireEvent, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { listItem } from "../../__mocks__/schedule-execution.mock";
import { ScheduleExecutionList } from "./schedule-execution-list";

const rows = [
  listItem(),
  listItem({ id: "e2", firedUtc: "2026-10-26T10:00:00.000Z", status: 502, outcome: "Failed", durationMs: 30_000 }),
  listItem({ id: "e3", firedUtc: null, finishedUtc: null, durationMs: null, status: null, outcome: "Failed" }),
];

const renderList = (props: Partial<React.ComponentProps<typeof ScheduleExecutionList>> = {}) => {
  const onSelect = vi.fn();
  const view = renderWithProviders(
    <ScheduleExecutionList executions={rows} isLoading={false} tz="UTC" selectedId={null} onSelect={onSelect} {...props} />,
  );
  return { ...view, onSelect };
};

describe("ScheduleExecutionList", () => {
  it("renders the columns and one row per execution", () => {
    renderList();
    for (const header of ["Fired at", "Result", "Status", "Duration"]) expect(screen.getByText(header)).toBeTruthy();
    expect(screen.getByText("Oct 26, 2026, 10:05:00")).toBeTruthy();
    expect(screen.getByText("HTTP 200")).toBeTruthy();
    expect(screen.getByText("HTTP 502")).toBeTruthy();
    expect(screen.getByText("340 ms")).toBeTruthy();
    expect(screen.getByText("30.0 s")).toBeTruthy();
    expect(screen.getAllByText("—").length).toBeGreaterThanOrEqual(3);
  });

  it("names each row for assistive tech", () => {
    renderList();
    expect(screen.getByRole("button", { name: "Open execution fired at Oct 26, 2026, 10:05:00" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Open execution fired at unknown time" })).toBeTruthy();
  });

  it("selects a row on click, Enter and Space, but not on other keys", async () => {
    const { onSelect } = renderList();
    const first = screen.getByRole("button", { name: /10:05:00/ });

    await userEvent.click(first);
    fireEvent.keyDown(first, { key: "Enter" });
    fireEvent.keyDown(first, { key: " " });
    fireEvent.keyDown(first, { key: "a" });

    expect(onSelect).toHaveBeenCalledTimes(3);
    expect(onSelect).toHaveBeenCalledWith("e1");
  });

  it("keeps the selected row highlighted", () => {
    renderList({ selectedId: "e2" });
    const cells = screen.getByText("HTTP 502").closest("tr")?.querySelectorAll("td") ?? [];
    expect(Array.from(cells).every((cell) => cell.className.includes("bg-muted/50"))).toBe(true);
  });

  it("shows 10 skeleton rows while loading", () => {
    const { container } = renderList({ isLoading: true });
    expect(container.querySelectorAll("tbody tr")).toHaveLength(10);
    expect(screen.queryByText("HTTP 200")).toBeNull();
  });
});
