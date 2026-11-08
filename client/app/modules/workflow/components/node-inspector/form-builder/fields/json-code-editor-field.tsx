"use client";

import { useMemo, useState } from "react";
import { Lock } from "lucide-react";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { FieldProps } from "../form-field.types";
import { ExpressionHighlighter } from "../utils/expression-highlighter";
import { pickerTarget, showsVariablePicker } from "./secret-picker";
import { useLockedValue } from "../utils/use-locked-value";
import {
  LockedKeys,
  shieldExpressions,
  withLockedKeys,
  withoutLockedKeys,
} from "../utils/json-locked-keys";
import { cn } from "@/lib/utils";
import { useT, tStatic } from "@seliseblocks/genesis-os";

const LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE = "logic.workflow.nodes.inspector";

const friendlyJsonError = (message: string): string => {
  if (message.includes("Unexpected end of JSON input")) {
    return tStatic(
      LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE,
      "JSON_CODE_EDITOR_FIELD.INCOMPLETE_JSON_CHECK_FOR",
    );
  }
  if (
    message.includes("Expected property name") ||
    message.includes("Expected double-quoted property name")
  ) {
    return tStatic(
      LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE,
      "JSON_CODE_EDITOR_FIELD.PROPERTY_NAMES_MUST_BE",
    );
  }
  const tokenMatch = /Unexpected token '?([^' ]+)'?/.exec(message);
  if (tokenMatch) {
    return tStatic(
      LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE,
      "JSON_CODE_EDITOR_FIELD.INVALID_SYNTAX_NEAR_CHECK",
      {
        value: tokenMatch[1],
      },
    );
  }
  return tStatic(
    LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE,
    "JSON_CODE_EDITOR_FIELD.INVALID_JSON_PLEASE_CHECK",
  );
};

const validateJson = (value: string): string | null => {
  const trimmed = value.trim();
  if (!trimmed) return null;
  // Expressions become placeholders, in or out of a string literal, so they never read as a syntax error.
  const sanitized = shieldExpressions(trimmed).shielded;
  try {
    JSON.parse(sanitized);
    return null;
  } catch (error) {
    return friendlyJsonError(error instanceof Error ? error.message : "");
  }
};

export const JsonCodeEditor = ({
  field,
  value,
  onChange,
  readOnly,
  variablePicker,
  data,
  config,
}: FieldProps<string>) => {
  const { t } = useT(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE);
  const [touched, setTouched] = useState(false);
  const { locked, lockedKey } = useLockedValue<LockedKeys>(field, data, config);
  const lockedKeys = locked && Object.keys(locked).length ? locked : null;

  // With locked keys the editor shows the saved body plus those keys, so it keeps its own text and
  // saves it with the locked keys stripped out. Re-seeded whenever new locked keys arrive.
  const [local, setLocal] = useState<{ key: string | null; text: string }>({ key: null, text: "" });
  if (lockedKeys && local.key !== lockedKey) {
    setLocal({ key: lockedKey, text: withLockedKeys(value || "", lockedKeys) });
  }
  const text = lockedKeys ? local.text : value || "";

  const handleChange = (next: string) => {
    if (!lockedKeys) return onChange(next);
    setLocal({ key: lockedKey, text: next });
    onChange(withoutLockedKeys(next, lockedKeys).value);
  };

  const jsonError = useMemo(() => validateJson(text), [text]);
  const showError = touched && Boolean(jsonError) && !field.disabled && !readOnly;
  const lockError =
    lockedKeys && !jsonError ? withoutLockedKeys(text, lockedKeys).error : undefined;

  return (
    <div className={cn("relative flex-1")}>
      <ExpressionHighlighter
        value={text}
        isMultiline={true}
        fontClassName="font-mono"
        variablePicker={
          // A pick replaces the whole value, which would wipe the locked keys.
          !lockedKeys && showsVariablePicker(field, readOnly, variablePicker)
            ? { target: pickerTarget(field), onChange: handleChange }
            : null
        }
      >
        <Textarea
          id={field.id}
          value={text}
          onChange={
            readOnly
              ? undefined
              : (e) => {
                  setTouched(true);
                  handleChange(e.target.value);
                }
          }
          onBlur={() => setTouched(true)}
          placeholder={field.placeholder}
          readOnly={readOnly}
          className={cn(
            "font-mono text-sm",
            (showError || lockError) &&
              "focus-visible:ring-red-600 focus focus-visible:border-input border-red-600",
          )}
          rows={field.height ? Math.floor(field.height / 24) : 10}
          disabled={field.disabled as boolean}
        />
      </ExpressionHighlighter>
      {showError && <p className="text-xs text-destructive mt-1">{jsonError}</p>}
      {lockError && <p className="text-xs text-destructive mt-1">{lockError}</p>}
      {lockedKeys && (
        <p className="mt-1 flex items-center gap-1 text-xs text-muted-foreground">
          <Lock aria-hidden="true" className="h-3 w-3" />
          {t("JSON_CODE_EDITOR_FIELD.LOCKED_CAN_T_BE", {
            value: Object.keys(lockedKeys).join(", "),
          })}
        </p>
      )}
    </div>
  );
};
