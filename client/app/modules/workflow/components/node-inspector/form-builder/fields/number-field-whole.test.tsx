import { createEvent, fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { NumberField } from "./number-field";
import type { FieldSchema } from "../form-field.types";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const config: any = {};

const WHOLE: FieldSchema = { id: "n", key: "n", type: "number", min: 1, step: 1, wholeNumber: true };

const renderField = (field: FieldSchema, onChange = vi.fn()) => {
  render(<NumberField field={field} value={2} onChange={onChange} data={{}} config={config} />);
  return { input: screen.getByRole("spinbutton"), onChange };
};

const keyIsBlocked = (input: HTMLElement, key: string) => {
  const event = createEvent.keyDown(input, { key });
  fireEvent(input, event);
  return event.defaultPrevented;
};

const pasteIsBlocked = (input: HTMLElement, text: string) => {
  const event = createEvent.paste(input, { clipboardData: { getData: () => text } });
  fireEvent(input, event);
  return event.defaultPrevented;
};

describe("NumberField wholeNumber (SPEC10 C8/C9)", () => {
  for (const commitOn of [undefined, "blur"] as const) {
    describe(commitOn ? "commitOn blur" : "immediate", () => {
      const field = { ...WHOLE, commitOn };

      it("blocks the decimal point, signs and the exponent", () => {
        const { input } = renderField(field);
        for (const key of [".", ",", "-", "+", "e", "E"]) expect(keyIsBlocked(input, key)).toBe(true);
      });

      it("lets digits, editing and navigation keys through", () => {
        const { input } = renderField(field);
        for (const key of ["0", "7", "Backspace", "Delete", "ArrowUp", "ArrowLeft", "Tab", "Enter"]) {
          expect(keyIsBlocked(input, key)).toBe(false);
        }
      });

      it("blocks a paste that is not only digits", () => {
        const { input } = renderField(field);
        for (const text of ["abc", "2.5", "-3", "1e2", " 4", ""]) expect(pasteIsBlocked(input, text)).toBe(true);
        expect(pasteIsBlocked(input, "42")).toBe(false);
      });

      it("ignores a non-digit value and saves digits as a whole number", () => {
        const { input, onChange } = renderField(field);
        fireEvent.change(input, { target: { value: "2.5" } });
        if (commitOn) fireEvent.blur(input);
        expect(onChange).not.toHaveBeenCalled();
        fireEvent.change(input, { target: { value: "12" } });
        if (commitOn) fireEvent.blur(input);
        expect(onChange).toHaveBeenCalledWith(12);
      });
    });
  }

  it("commitOn blur: clearing the field and typing a single digit saves that digit (Limit's Max Items)", () => {
    const onChange = vi.fn();
    render(<NumberField field={{ ...WHOLE, commitOn: "blur" }} value={1} onChange={onChange} data={{}} config={config} />);
    const input = screen.getByRole("spinbutton") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "" } });
    expect(input.value).toBe("");
    expect(onChange).not.toHaveBeenCalled();
    fireEvent.change(input, { target: { value: "2" } });
    expect(input.value).toBe("2");
    fireEvent.blur(input);
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith(2);
  });

  it("without wholeNumber, the decimal point and minus still reach the input", () => {
    const onChange = vi.fn();
    const { input } = renderField({ id: "n", key: "n", type: "number" }, onChange);
    expect(keyIsBlocked(input, ".")).toBe(false);
    expect(keyIsBlocked(input, "-")).toBe(false);
    expect(pasteIsBlocked(input, "2.5")).toBe(false);
    fireEvent.change(input, { target: { value: "2.5" } });
    expect(onChange).toHaveBeenCalledWith(2.5);
  });
});
