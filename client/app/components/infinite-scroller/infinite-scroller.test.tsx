import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { InfiniteScroll } from "./infinite-scroller";

const baseProps = () => ({
  renderItem: (item: string, i: number) => <div key={i}>{item}</div>,
  topFn: vi.fn().mockResolvedValue([]),
  pollingFn: vi.fn().mockResolvedValue([]),
  pollingInterval: 1000,
  loadingIndicator: <div>loading...</div>,
  bottomIndicator: (cb: () => void) => (
    <button onClick={cb}>jump-to-bottom</button>
  ),
  hasTopMore: true,
});

describe("InfiniteScroll", () => {
  afterEach(() => vi.restoreAllMocks());

  it("renders the empty state when there is no data", () => {
    render(<InfiniteScroll<string> {...baseProps()} initialData={[]} />);
    expect(screen.getByText("No logs found")).toBeTruthy();
  });

  it("renders the initial items", () => {
    render(
      <InfiniteScroll<string> {...baseProps()} initialData={["a", "b"]} />,
    );
    expect(screen.getByText("a")).toBeTruthy();
    expect(screen.getByText("b")).toBeTruthy();
  });

  it("fetches older data when scrolled to the top", async () => {
    const props = baseProps();
    props.topFn = vi.fn().mockResolvedValue(["old1"]);
    const { container } = render(
      <InfiniteScroll<string> {...props} initialData={["b"]} />,
    );
    const scroller = container.querySelector(".overflow-scroll") as HTMLElement;
    Object.defineProperty(scroller, "scrollTop", { value: 0, configurable: true });
    fireEvent.scroll(scroller);
    await waitFor(() => expect(props.topFn).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByText("old1")).toBeTruthy());
  });

  it("stops fetching older data when the top function returns nothing", async () => {
    const props = baseProps();
    props.topFn = vi.fn().mockResolvedValue([]);
    const { container } = render(
      <InfiniteScroll<string> {...props} initialData={["b"]} />,
    );
    const scroller = container.querySelector(".overflow-scroll") as HTMLElement;
    Object.defineProperty(scroller, "scrollTop", { value: 0, configurable: true });
    fireEvent.scroll(scroller);
    await waitFor(() => expect(props.topFn).toHaveBeenCalledTimes(1));
  });

  it("polls for newer data and surfaces the bottom indicator", async () => {
    vi.useFakeTimers();
    const props = baseProps();
    props.pollingFn = vi.fn().mockResolvedValue(["new1"]);
    render(<InfiniteScroll<string> {...props} initialData={["a"]} />);
    await vi.advanceTimersByTimeAsync(1000);
    expect(props.pollingFn).toHaveBeenCalled();
    vi.useRealTimers();
    await waitFor(() => expect(screen.getByText("new1")).toBeTruthy());
    expect(screen.getByText("jump-to-bottom")).toBeTruthy();
  });

  it("scrolls to the bottom when the indicator is clicked", async () => {
    vi.useFakeTimers();
    const props = baseProps();
    props.pollingFn = vi.fn().mockResolvedValue(["new1"]);
    const { container } = render(
      <InfiniteScroll<string> {...props} initialData={["a"]} />,
    );
    const scroller = container.querySelector(".overflow-scroll") as HTMLElement;
    const scrollTo = vi.fn();
    scroller.scrollTo = scrollTo as never;
    await vi.advanceTimersByTimeAsync(1000);
    vi.useRealTimers();
    const btn = await screen.findByText("jump-to-bottom");
    fireEvent.click(btn);
    expect(scrollTo).toHaveBeenCalled();
  });
});
