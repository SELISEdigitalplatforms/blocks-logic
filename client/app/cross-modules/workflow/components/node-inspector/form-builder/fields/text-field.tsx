"use client";

import { useState } from "react";
import { Input } from "@/components/ui-kits/input/input";
import { Button } from "@/components/ui-kits/button/button";
import { Copy, Check } from "lucide-react";
import { FieldProps } from "../form-field.types";
import { ExpressionHighlighter } from "../utils/expression-highlighter";

export const TextField = ({ field, value, onChange, readOnly }: FieldProps<string>) => {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    await navigator.clipboard.writeText(String(value || ""));
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="relative">
      <ExpressionHighlighter value={value || ""} isMultiline={false}>
        <Input
          id={field.id}
          type="text"
          value={value || ""}
          onChange={readOnly ? undefined : (e) => onChange(e.target.value)}
          placeholder={field.placeholder}
          disabled={field.disabled as boolean}
          readOnly={readOnly}
          required={field.required as boolean}
          maxLength={field.maxLength}
          minLength={field.minLength}
          className={field.copyable ? "pr-10" : ""}
        />
      </ExpressionHighlighter>
      {field.copyable && (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="absolute right-1 top-1/2 h-7 w-7 -translate-y-1/2 p-0"
          onClick={handleCopy}
        >
          {copied ? <Check className="h-4 w-4 text-green-500" /> : <Copy className="h-4 w-4" />}
        </Button>
      )}
    </div>
  );
};
