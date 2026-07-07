"use client";

import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { FieldProps } from "../form-field.types";
import { ExpressionHighlighter } from "../utils/expression-highlighter";
import { cn } from "@/lib/utils";

export const TextareaField = ({ field, value, onChange, readOnly, }: FieldProps<string>) => {
  return (
    <div className={cn("relative flex-1")}>
      <ExpressionHighlighter value={value || ""} isMultiline={true}>
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
