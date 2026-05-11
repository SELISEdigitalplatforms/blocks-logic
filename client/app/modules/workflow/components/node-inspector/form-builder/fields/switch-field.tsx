"use client";
import { Switch } from "@/components/ui-kits/switch/switch";
import { FieldProps } from "../form-field.types";

export const SwitchField = ({ field, value, onChange, readOnly }: FieldProps<boolean>) => {
  return (
    <Switch
      id={field.id}
      checked={value as boolean}
      onCheckedChange={readOnly ? undefined : (checked) => onChange(checked)}
      disabled={field.disabled as boolean}
    />
  );
};
