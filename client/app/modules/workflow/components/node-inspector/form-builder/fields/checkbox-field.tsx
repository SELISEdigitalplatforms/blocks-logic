"use client";

import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Label } from "@/components/ui-kits/label/label";
import { FieldProps } from "../form-field.types";

export const CheckboxField = ({ field, value, onChange, readOnly }: FieldProps<boolean>) => {
  return (
    <div className="flex items-center space-x-2">
      <Checkbox
        id={field.id}
        checked={value as boolean}
        onCheckedChange={readOnly ? undefined : (checked) => onChange(checked as boolean)}
        disabled={field.disabled as boolean}
      />
      <Label htmlFor={field.id} className="cursor-pointer font-normal">
        {field.placeholder || "Enable"}
      </Label>
    </div>
  );
};
