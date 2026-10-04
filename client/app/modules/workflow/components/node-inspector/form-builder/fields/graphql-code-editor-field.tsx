"use client";

import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { cn } from "@/lib/utils";
import { FieldProps } from "../form-field.types";
import { ExpressionHighlighter } from "../utils/expression-highlighter";
import { pickerTarget, showsVariablePicker } from "./secret-picker";

export const GraphqlCodeEditor = ({
  field,
  value,
  onChange,
  readOnly,
  variablePicker,
}: FieldProps<string>) => {
  return (
    <div className={cn("relative flex-1")}>
      <ExpressionHighlighter
        value={value || ""}
        isMultiline={true}
        fontClassName="font-mono"
        variablePicker={
          showsVariablePicker(field, readOnly, variablePicker)
            ? { target: pickerTarget(field), onChange: (next) => onChange(next) }
            : null
        }
      >
        <Textarea
          id={field.id}
          value={value || ""}
          onChange={
            readOnly
              ? undefined
              : (e) => {
                  onChange(e.target.value);
                }
          }
          placeholder={field.placeholder}
          readOnly={readOnly}
          className="font-mono text-sm"
          rows={field.height ? Math.floor(field.height / 24) : 10}
          disabled={field.disabled as boolean}
        />
      </ExpressionHighlighter>
    </div>
  );
};
