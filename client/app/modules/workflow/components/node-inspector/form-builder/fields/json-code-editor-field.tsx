"use client";

import { useMemo, useState } from "react";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { FieldProps } from "../form-field.types";
import { ExpressionHighlighter } from "../utils/expression-highlighter";
import { cn } from "@/lib/utils";

const EXPRESSION_REGEX = /\{\{\$.*?\}\}/g;

const friendlyJsonError = (message: string): string => {
  if (message.includes("Unexpected end of JSON input")) {
    return "Incomplete JSON — check for missing brackets, braces, or quotes.";
  }
  if (
    message.includes("Expected property name") ||
    message.includes("Expected double-quoted property name")
  ) {
    return "Property names must be wrapped in double quotes.";
  }
  const tokenMatch = message.match(/Unexpected token '?([^' ]+)'?/);
  if (tokenMatch) {
    return `Invalid syntax near "${tokenMatch[1]}" — check for missing commas, colons, or quotes.`;
  }
  return "Invalid JSON — please check the format.";
};

const validateJson = (value: string): string | null => {
  const trimmed = value.trim();
  if (!trimmed) return null;
  const sanitized = trimmed.replace(EXPRESSION_REGEX, '"__expression__"');
  try {
    JSON.parse(sanitized);
    return null;
  } catch (error) {
    return friendlyJsonError(error instanceof Error ? error.message : "");
  }
};

export const JsonCodeEditor = ({ field, value, onChange, readOnly }: FieldProps<string>) => {
  const [touched, setTouched] = useState(false);
  const jsonError = useMemo(() => validateJson(value || ""), [value]);
  const showError = touched && Boolean(jsonError) && !field.disabled && !readOnly;

  return (
    <div className={cn("relative flex-1")}>
      <ExpressionHighlighter value={value || ""} isMultiline={true} fontClassName="font-mono">
        <Textarea
          id={field.id}
          value={value || ""}
          onChange={
            readOnly
              ? undefined
              : (e) => {
                  setTouched(true);
                  onChange(e.target.value);
                }
          }
          onBlur={() => setTouched(true)}
          placeholder={field.placeholder}
          className={cn(
            "font-mono text-sm",
            showError &&
              "focus-visible:ring-red-600 focus focus-visible:border-input border-red-600",
          )}
          rows={field.height ? Math.floor(field.height / 24) : 10}
          disabled={field.disabled as boolean}
        />
      </ExpressionHighlighter>
      {showError && <p className="text-xs text-destructive mt-1">{jsonError}</p>}
    </div>
  );
};
