"use client";

import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { FieldProps } from "../form-field.types";

export const CodeEditorField = ({ field, value, onChange, readOnly }: FieldProps<string>) => {
  return (
    <Textarea
      id={field.id}
      value={value || ""}
      onChange={readOnly ? undefined : (e) => onChange(e.target.value)}
      placeholder={field.placeholder}
      className="font-mono text-sm"
      rows={field.height ? Math.floor(field.height / 24) : 10}
      disabled={field.disabled as boolean}
    />
  );
};
