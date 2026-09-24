"use client";

import { VariableInsertMenu } from "@/components/variable-insert-menu";
import { useSecrets } from "@/hooks/use-secrets";
import { buildVarToken } from "@/lib/var-token";
import { FieldSchema, FormFieldType } from "../form-field.types";

/**
 * Field types whose value input(s) get the `{{$VAR.name}}` picker. Key inputs never do, and neither do
 * pickers, toggles, numbers or code editors, where replacing the whole value makes no sense.
 */
const VARIABLE_PICKER_TYPES: ReadonlySet<FormFieldType> = new Set<FormFieldType>([
  "text",
  "textarea",
  "expression",
  "key-value-pairs",
  "fixed-key-value-pairs",
  "key-type-value-pairs",
  "expression-list",
]);

/**
 * Whether `field`'s value input(s) show the secret picker: on by default for eligible types, off
 * with `variablePicker: false`, and always hidden while the field is read-only or disabled.
 */
export const showsVariablePicker = (field: FieldSchema, readOnly?: boolean) =>
  VARIABLE_PICKER_TYPES.has(field.type) &&
  field.variablePicker !== false &&
  !readOnly &&
  field.disabled !== true;

type Props = {
  /** Receives the `{{$VAR.name}}` token. It replaces the whole value, as the proxy form does. */
  onPick: (token: string) => void;
  /** What the picker fills, for its accessible name, e.g. "Body" or "Headers row 2". */
  target: string;
};

/**
 * The tenant's secrets as a `{{$VAR.name}}` menu for one value input. Each picker calls
 * `useSecrets()` itself; react-query shares the one fetch between them.
 */
export const SecretPicker = ({ onPick, target }: Props) => {
  const { data, isLoading, isError } = useSecrets();

  return (
    <VariableInsertMenu
      variables={data}
      variablesLoading={isLoading}
      variablesError={isError}
      onPick={(name) => onPick(buildVarToken(name))}
      ariaLabel={`Insert a configuration variable into ${target}`}
    />
  );
};

/** Label used in the picker's accessible name. */
export const pickerTarget = (field: FieldSchema, suffix?: string) =>
  [field.label || field.key, suffix].filter(Boolean).join(" ");
