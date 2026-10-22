import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { NumberField } from "./number-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const config: any = {};

describe("NumberField commitOn blur", () => {
  it("reports nothing while typing, then once on blur", () => {
    const onChange = vi.fn();
    render(<NumberField field={{ id: "n", key: "n", type: "number", commitOn: "blur" }} value={2} onChange={onChange} data={{}} config={config} />);
    const input = screen.getByRole("spinbutton");
    fireEvent.change(input, { target: { value: "1" } });
    fireEvent.change(input, { target: { value: "12" } });
    expect(onChange).not.toHaveBeenCalled();
    fireEvent.blur(input);
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith(12);
  });

  it("commits on Enter, and not at all when the value did not change", () => {
    const onChange = vi.fn();
    render(<NumberField field={{ id: "n", key: "n", type: "number", commitOn: "blur" }} value={2} onChange={onChange} data={{}} config={config} />);
    const input = screen.getByRole("spinbutton");
    fireEvent.blur(input);
    expect(onChange).not.toHaveBeenCalled();
    fireEvent.change(input, { target: { value: "5" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onChange).toHaveBeenCalledWith(5);
  });

  it("default number fields still report every keystroke", () => {
    const onChange = vi.fn();
    render(<NumberField field={{ id: "n", key: "n", type: "number" }} value={2} onChange={onChange} data={{}} config={config} />);
    fireEvent.change(screen.getByRole("spinbutton"), { target: { value: "1" } });
    expect(onChange).toHaveBeenCalledWith(1);
  });
});
