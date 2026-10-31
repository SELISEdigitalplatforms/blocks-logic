"use client";

import { useState, type ClipboardEvent, type KeyboardEvent } from "react";
import { Input } from "@/components/ui-kits/input/input";
import { FieldProps } from "../form-field.types";

const toNumber = (text: string) => (text ? Number(text) : null);

/** Keys a number input accepts that are not digits: the decimal point, signs and the exponent. */
const NON_DIGIT_KEYS = new Set([".", ",", "-", "+", "e", "E"]);
const DIGITS_ONLY = /^\d*$/;

/** Whole-number mode: a decimal point, sign or exponent can never be typed or pasted. */
const blockNonDigitKey = (e: KeyboardEvent<HTMLInputElement>) => {
  if (NON_DIGIT_KEYS.has(e.key)) e.preventDefault();
};

const blockNonDigitPaste = (e: ClipboardEvent<HTMLInputElement>) => {
  if (!/^\d+$/.test(e.clipboardData.getData("text"))) e.preventDefault();
};

/** Keeps a local draft and reports it only on blur or Enter, so a partial value ("1" on the way to "12") is never saved. */
const CommitOnBlurNumberInput = ({ field, value, onChange, readOnly }: FieldProps<number | null>) => {
  const [draft, setDraft] = useState(value ?? "");
  const [shown, setShown] = useState(value);
  if (value !== shown) {
    // The stored value changed from outside (load, undo, coercion): show it.
    setShown(value);
    setDraft(value ?? "");
  }

  const commit = () => {
    if (readOnly) return;
    const next = toNumber(String(draft));
    if (next !== (value ?? null)) onChange(next);
  };

  return (
    <Input
      id={field.id}
      type="number"
      value={draft}
      onChange={
        readOnly
          ? undefined
          : (e) => {
              if (field.wholeNumber && !DIGITS_ONLY.test(e.target.value)) return;
              setDraft(e.target.value);
            }
      }
      onBlur={commit}
      onKeyDown={(e) => {
        if (field.wholeNumber) blockNonDigitKey(e);
        if (e.key === "Enter") commit();
      }}
      onPaste={field.wholeNumber ? blockNonDigitPaste : undefined}
      placeholder={field.placeholder}
      min={field.min}
      max={field.max}
      step={field.step}
      disabled={field.disabled as boolean}
    />
  );
};

export const NumberField = (props: FieldProps<number | null>) => {
  if (props.field.commitOn === "blur") return <CommitOnBlurNumberInput {...props} />;
  const { field, value, onChange, readOnly } = props;
  return (
    <Input
      id={field.id}
      type="number"
      value={value ?? ""}
      onChange={
        readOnly
          ? undefined
          : (e) => {
              if (field.wholeNumber && !DIGITS_ONLY.test(e.target.value)) return;
              onChange(toNumber(e.target.value));
            }
      }
      onKeyDown={field.wholeNumber ? blockNonDigitKey : undefined}
      onPaste={field.wholeNumber ? blockNonDigitPaste : undefined}
      placeholder={field.placeholder}
      min={field.min}
      max={field.max}
      step={field.step}
      disabled={field.disabled as boolean}
    />
  );
};
