"use client";

import { FieldProps } from "../form-field.types";
import type { Condition } from "./conditions-field";
import { SwitchConditionList } from "./switch-condition-list";

/** What the Filter field reports: both top-level parameters, which the schema's onChange saves. */
export type FilterConditionsValue = { conditions: Condition[]; conditionType: unknown };

// .NET char.IsWhiteSpace, which string.Trim and the enum parser use. Unlike JS \s it has U+0085 and not U+FEFF.
const DOTNET_SPACE = "[\\t\\n\\v\\f\\r \\u0085\\u00a0\\u1680\\u2000-\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000]";
const TRIM = new RegExp(`^${DOTNET_SPACE}+|${DOTNET_SPACE}+$`, "g");
// Int32.Parse (NumberStyles.Integer) after Trim: a sign, ASCII digits, trailing white space and trailing NULs.
const INTEGER_TEXT = /^[+-]?[0-9]+[\t\n\v\f\r ]*\0*$/;
const NAME = /^(and|or)$/i;
const TWO_POW_32 = 2 ** 32;

/**
 * Whether the server's Filter parameters bind this conditionType to Or (Newtonsoft 13 StringEnumConverter
 * on FilterConditionType { And = 0, Or = 1 }, checked against the library):
 * - a JSON integer whose low 32 bits are 1 (the converter casts the Int64 to the enum unchecked);
 * - integer text equal to 1, e.g. "1", " +01 ";
 * - "and"/"or" names in any case, comma-separated, trimmed, with at least one "or" (the enum is parsed as
 *   flags, so "and,or" is And | Or = Or).
 * Everything else is And, an undefined number, or a parse failure; the editor shows AND for all of those.
 */
export const isOrConditionType = (value: unknown): boolean => {
  if (typeof value === "number") return Number.isSafeInteger(value) && ((value % TWO_POW_32) + TWO_POW_32) % TWO_POW_32 === 1;
  if (typeof value !== "string") return false;
  const text = value.replace(TRIM, "");
  if (INTEGER_TEXT.test(text)) return Number(text.replace(/[\t\n\v\f\r \0]+$/, "")) === 1;
  const names = text.split(",").map((part) => part.replace(TRIM, ""));
  return names.every((name) => NAME.test(name)) && names.some((name) => name.toLowerCase() === "or");
};

/**
 * The Filter node's conditions: Switch's condition list on its own, with no rule card. Reads and writes the
 * node's top-level `conditions` and `conditionType`. The list reports "all" | "or"; Filter saves "and" | "or"
 * (the server's enum), so only this adapter maps "all" to "and". The saved value is only normalised for
 * display (every value the server binds to Or shows OR, anything else AND); it is saved back unchanged until
 * the toggle is used.
 */
export const FilterConditionsField = ({ field, onChange, readOnly, data }: FieldProps<FilterConditionsValue>) => {
  const conditions = Array.isArray(data.conditions) ? (data.conditions as Condition[]) : [];
  const conditionType = data.conditionType;

  return (
    <SwitchConditionList
      // The schema field has no label (the list shows its own heading); the variable picker names its
      // target from the label, so give it the heading instead of the internal key.
      field={{ ...field, label: field.label || "Conditions" }}
      idPrefix={field.id}
      conditions={conditions}
      conditionType={isOrConditionType(conditionType) ? "or" : "and"}
      onChange={(next) => onChange({ conditions: next, conditionType })}
      onConditionTypeChange={(next) => onChange({ conditions, conditionType: next === "or" ? "or" : "and" })}
      readOnly={readOnly}
      locked={readOnly || !!field.disabled}
    />
  );
};
