import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { RateLimitCard } from "./rate-limit-card";

describe("RateLimitCard (FN-19)", () => {
  it("a public function shows the 600 default until the tenant sets a number", () => {
    render(<RateLimitCard authMode="Public" value={null} onChange={vi.fn()} />);

    expect(screen.getByTestId("rate-limit-effective").textContent).toContain("600 calls a minute");
    expect(screen.getByPlaceholderText("Default: 600")).toBeTruthy();
  });

  it("a function that needs a login has no limit by default", () => {
    render(<RateLimitCard authMode="Token" value={undefined} onChange={vi.fn()} />);

    expect(screen.getByTestId("rate-limit-effective").textContent).toContain("No limit");
  });

  it("a whole number is saved, an empty field goes back to the default", () => {
    const onChange = vi.fn();
    render(<RateLimitCard authMode="Public" value={null} onChange={onChange} />);
    const input = screen.getByTestId("rate-limit-input");

    fireEvent.change(input, { target: { value: "120" } });
    expect(onChange).toHaveBeenLastCalledWith(120);

    fireEvent.change(input, { target: { value: "" } });
    expect(onChange).toHaveBeenLastCalledWith(null);
  });

  it.each(["0", "-3", "1.5", "100001"])("%s is refused with a message and not saved", (text) => {
    const onChange = vi.fn();
    render(<RateLimitCard authMode="Public" value={null} onChange={onChange} />);

    fireEvent.change(screen.getByTestId("rate-limit-input"), { target: { value: text } });

    expect(onChange).not.toHaveBeenCalled();
    expect(screen.getByRole("alert").textContent).toContain("1 to 100,000");
  });
});
