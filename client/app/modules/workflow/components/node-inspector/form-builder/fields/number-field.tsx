"use client";

import { Input } from "@/components/ui-kits/input/input";
import { FieldProps } from "../form-field.types";

export const NumberField = ({ field, value, onChange, readOnly }: FieldProps<number | null>) => {
  return (
    <Input
      id={field.id}
      type="number"
      value={value ?? ""}
      onChange={
        readOnly ? undefined : (e) => onChange(e.target.value ? Number(e.target.value) : null)
      }
      placeholder={field.placeholder}
      min={field.min}
      max={field.max}
      step={field.step}
      disabled={field.disabled as boolean}
    />
  );
};
