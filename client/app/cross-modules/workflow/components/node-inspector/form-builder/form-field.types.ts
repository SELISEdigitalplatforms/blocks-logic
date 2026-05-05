export type FieldType =
  | "text"
  | "textarea"
  | "number"
  | "select"
  | "multiselect"
  | "checkbox"
  | "switch"
  | "radio"
  | "code-editor"
  | "key-value-pairs"
  | "conditions"
  | "array"
  | "expression"
  | "display"
  | "schema-fields"
  | "schema-field-picker";

export interface SelectOption {
  value: string;
  label: string;
}

export interface FieldSchema<Whole = Record<string, unknown>> {
  id: string;
  type: FieldType;
  key: string;
  label?: string;
  info?: string;
  placeholder?: string;
  defaultValue?: unknown | ((data: Whole) => unknown);

  disabled?: boolean | ((data: Whole) => boolean);
  hidden?: boolean | ((data: Whole) => boolean);
  required?: boolean | ((data: Whole) => boolean);

  displayValue?: (
    data: Whole,
    config: { projectKey: string; workflowId: string; nodeId: string },
  ) => unknown;
  onChange?: (value: unknown, data: Whole) => Whole | void;
  options?:
    | SelectOption[]
    | ((
        data: Whole,
        config: { projectKey: string; workflowId: string },
      ) => Promise<SelectOption[]>);
  copyable?: boolean;
  maxLength?: number;
  minLength?: number;
  min?: number;
  max?: number;
  step?: number;
  language?: "json" | "javascript" | "html" | "css";
  height?: number;
  keyLabel?: string;
  valueLabel?: string;
  addButtonText?: string;
  itemType?: FieldType;
  searchable?: boolean;
  prefix?: string;
  className?: string;
  dependsOn?: {
    key: string;
    value: unknown | unknown[];
    operator?: "equals" | "notEquals" | "in";
  };
  transient?: boolean;
}

export interface FieldProps<T = unknown> {
  field: FieldSchema;
  value: T;
  onChange: (value: T) => void;
  data: Record<string, unknown>;
  config: { projectKey: string; workflowId: string; nodeId: string };
  readOnly?: boolean;
  className?: string;
}

export type FormField = FieldSchema;
export type FieldComponentProps<T = unknown> = FieldProps<T>;
