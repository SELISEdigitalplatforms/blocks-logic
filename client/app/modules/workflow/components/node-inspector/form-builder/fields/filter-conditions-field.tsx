"use client";

import { FieldProps } from "../form-field.types";
import type { Condition } from "./conditions-field";
import { SwitchConditionList } from "./switch-condition-list";

/** What the Filter field reports: both top-level parameters, which the schema's onChange saves. */
export type FilterConditionsValue = { conditions: Condition[]; conditionType: unknown };

// .NET char.IsWhiteSpace, which string.Trim and the enum parser use. Unlike JS \s it has U+0085 and not U+FEFF.
// Matched one code unit at a time (no regex), so trimming stays linear on any input.
const DOTNET_SPACES = new Set(["\t", "\n", "\v", "\f", "\r", " ", "\u0085", "\u00a0", "\u1680", "\u2028", "\u2029", "\u202f", "\u205f", "\u3000"]);
const isDotnetSpace = (ch: string) => DOTNET_SPACES.has(ch) || (ch >= "\u2000" && ch <= "\u200a");

/** string.Trim(): drops .NET white space from both ends. */
const dotnetTrim = (text: string): string => {
  let start = 0;
  let end = text.length;
  while (start < end && isDotnetSpace(text.charAt(start))) start++;
  while (end > start && isDotnetSpace(text.charAt(end - 1))) end--;
  return text.slice(start, end);
};

// Int32.Parse (NumberStyles.Integer) after Trim: a sign, ASCII digits, trailing white space and trailing NULs.
const INTEGER_TRAILING_SPACES = new Set(["\t", "\n", "\v", "\f", "\r", " "]);
const isAsciiDigit = (ch: string) => ch >= "0" && ch <= "9";

/** The value of trimmed integer text as Int32.Parse reads it, or undefined when the text is not integer text. */
const parseIntegerText = (text: string): number | undefined => {
  let i = text.charAt(0) === "+" || text.charAt(0) === "-" ? 1 : 0;
  const digitsStart = i;
  while (i < text.length && isAsciiDigit(text.charAt(i))) i++;
  if (i === digitsStart) return undefined;
  const value = Number(text.slice(0, i));
  while (i < text.length && INTEGER_TRAILING_SPACES.has(text.charAt(i))) i++;
  while (i < text.length && text.charAt(i) === "\0") i++;
  return i === text.length ? value : undefined;
};

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
  const text = dotnetTrim(value);
  const integer = parseIntegerText(text);
  if (integer !== undefined) return integer === 1;
  const names = text.split(",").map((part) => dotnetTrim(part));
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
