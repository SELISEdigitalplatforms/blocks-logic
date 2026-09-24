"use client";

import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { FieldProps } from "../form-field.types";
import { ExpressionHighlighter } from "../utils/expression-highlighter";
import { cn } from "@/lib/utils";
import { pickerTarget, SecretPicker, showsVariablePicker } from "./secret-picker";

export const TextareaField = ({ field, value, onChange, readOnly, }: FieldProps<string>) => {
  const picker = showsVariablePicker(field, readOnly);

  return (
    <div className={cn("relative flex-1")}>
      {picker && (
        <div className="absolute right-1 top-1 z-10">
          <SecretPicker target={pickerTarget(field)} onPick={onChange} />
        </div>
      )}
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
        className={cn(picker && "pr-14")}
      />
    </ExpressionHighlighter>
    </div>
  );
};
