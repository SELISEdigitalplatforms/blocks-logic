import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { SearchInput } from "./search-input";

describe("SearchInput", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  const input = () => screen.getByRole("textbox", { name: "Search runs" }) as HTMLInputElement;

  it("debounces typing into one call with the last value", () => {
    const onChange = vi.fn();
    render(<SearchInput value="" onChange={onChange} aria-label="Search runs" />);
    fireEvent.change(input(), { target: { value: "a" } });
    fireEvent.change(input(), { target: { value: "ab" } });
    expect(onChange).not.toHaveBeenCalled();
    act(() => vi.advanceTimersByTime(300));
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith("ab");
  });

  it("calls the latest onChange the parent passed, not the first", () => {
    const first = vi.fn();
    const second = vi.fn();
    const { rerender } = render(<SearchInput value="" onChange={first} aria-label="Search runs" />);
    rerender(<SearchInput value="" onChange={second} aria-label="Search runs" />);
    fireEvent.change(input(), { target: { value: "x" } });
    act(() => vi.advanceTimersByTime(300));
    expect(first).not.toHaveBeenCalled();
    expect(second).toHaveBeenCalledWith("x");
  });

  it("follows the value when the parent changes it", () => {
    const { rerender } = render(
      <SearchInput value="old" onChange={vi.fn()} aria-label="Search runs" />,
    );
    expect(input().value).toBe("old");
    rerender(<SearchInput value="new" onChange={vi.fn()} aria-label="Search runs" />);
    expect(input().value).toBe("new");
  });

  it("clearing cancels a pending search so the old text does not come back", () => {
    const onChange = vi.fn();
    render(<SearchInput value="abc" onChange={onChange} aria-label="Search runs" />);
    fireEvent.change(input(), { target: { value: "abcd" } });
    fireEvent.click(screen.getByRole("button"));
    act(() => vi.advanceTimersByTime(300));
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith("");
    expect(input().value).toBe("");
  });

  it("cancels a pending search on unmount", () => {
    const onChange = vi.fn();
    const { unmount } = render(
      <SearchInput value="" onChange={onChange} aria-label="Search runs" />,
    );
    fireEvent.change(input(), { target: { value: "x" } });
    unmount();
    act(() => vi.advanceTimersByTime(300));
    expect(onChange).not.toHaveBeenCalled();
  });
});
