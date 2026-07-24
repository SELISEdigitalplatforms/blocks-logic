import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  ChipsInput,
  ChipsInputField,
  ChipsInputList,
  useChipsContext,
} from "./chips-input";

const Harness = (props: {
  value: string[];
  onChange: (v: string[]) => void;
  validatorRegex?: RegExp;
  customValidator?: (v: string) => boolean;
}) => (
  <ChipsInput {...props}>
    <ChipsInputList />
    <ChipsInputField />
  </ChipsInput>
);

describe("ChipsInput", () => {
  it("renders existing chips and removes one on click", () => {
    const onChange = vi.fn();
    render(<Harness value={["a", "b"]} onChange={onChange} />);
    expect(screen.getByText("a")).toBeTruthy();
    fireEvent.click(screen.getByLabelText("Remove a"));
    expect(onChange).toHaveBeenCalledWith(["b"]);
  });

  it("adds a chip on Enter and clears the input", () => {
    const onChange = vi.fn();
    render(<Harness value={[]} onChange={onChange} />);
    const input = screen.getByPlaceholderText("Type and press enter");
    fireEvent.change(input, { target: { value: "new" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onChange).toHaveBeenCalledWith(["new"]);
  });

  it("shows a validation error and blocks adding when regex fails", () => {
    const onChange = vi.fn();
    render(
      <Harness value={[]} onChange={onChange} validatorRegex={/^\d+$/} />,
    );
    const input = screen.getByPlaceholderText("Type and press enter");
    fireEvent.change(input, { target: { value: "abc" } });
    expect(screen.getByText("Invalid format")).toBeTruthy();
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onChange).not.toHaveBeenCalled();
  });

  it("uses a custom validator when provided", () => {
    const onChange = vi.fn();
    const customValidator = vi.fn((v: string) => v.length > 2);
    render(
      <Harness value={[]} onChange={onChange} customValidator={customValidator} />,
    );
    const input = screen.getByPlaceholderText("Type and press enter");
    fireEvent.change(input, { target: { value: "ok-value" } });
    expect(customValidator).toHaveBeenCalled();
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onChange).toHaveBeenCalledWith(["ok-value"]);
  });

  it("clears the error when the input is emptied", () => {
    const onChange = vi.fn();
    render(<Harness value={[]} onChange={onChange} validatorRegex={/^\d+$/} />);
    const input = screen.getByPlaceholderText("Type and press enter");
    fireEvent.change(input, { target: { value: "x" } });
    expect(screen.getByText("Invalid format")).toBeTruthy();
    fireEvent.change(input, { target: { value: "" } });
    expect(screen.queryByText("Invalid format")).toBeNull();
  });

  it("throws when the context hook is used outside the provider", () => {
    const Bad = () => {
      useChipsContext();
      return null;
    };
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    expect(() => render(<Bad />)).toThrow(/within <ChipsInput>/);
    spy.mockRestore();
  });
});
