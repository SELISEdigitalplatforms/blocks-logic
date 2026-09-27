"use client";

import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { FieldProps } from "../form-field.types";
import { ExpressionHighlighter } from "../utils/expression-highlighter";
import { cn } from "@/lib/utils";
import { pickerTarget, showsVariablePicker } from "./secret-picker";

export const TextareaField = ({ field, value, onChange, readOnly, variablePicker }: FieldProps<string>) => {
  const picker = showsVariablePicker(field, readOnly, variablePicker);

  return (
    <div className={cn("relative flex-1")}>
      <ExpressionHighlighter
        value={value || ""}
        isMultiline={true}
        variablePicker={picker ? { target: pickerTarget(field), onChange } : null}
      >
      <Textarea
        id={field.id}
        value={value || ""}
        onChange={readOnly ? undefined : (e) => {
          onChange(e.target.value);
        }}
        placeholder={field.placeholder}
        readOnly={readOnly}
        rows={4}
        maxLength={field.maxLength}
        minLength={field.minLength}
        disabled={field.disabled as boolean}
      />
    </ExpressionHighlighter>
    </div>
  );
};
