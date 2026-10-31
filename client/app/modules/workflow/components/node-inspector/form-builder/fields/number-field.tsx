"use client";

import { useState } from "react";
import { Input } from "@/components/ui-kits/input/input";
import { FieldProps } from "../form-field.types";

const toNumber = (text: string) => (text ? Number(text) : null);

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
      onChange={readOnly ? undefined : (e) => setDraft(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => {
        if (e.key === "Enter") commit();
      }}
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
        readOnly ? undefined : (e) => onChange(toNumber(e.target.value))
      }
      placeholder={field.placeholder}
      min={field.min}
      max={field.max}
      step={field.step}
      disabled={field.disabled as boolean}
    />
  );
};
