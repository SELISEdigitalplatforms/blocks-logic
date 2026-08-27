import { WorkflowStore } from "@/modules/workflow/store";

export type FormFieldType =
  | "text"
  | "textarea"
  | "number"
  | "select"
  | "select-with-description"
  | "multiselect"
  | "conditional-multiselect"
  | "checkbox"
  | "switch"
  | "radio"
  | "json-code-editor"
  | "graphql-code-editor"
  | "code-editor"
  | "key-value-pairs"
  | "fixed-key-value-pairs"
  | "key-type-value-pairs"
  | "array"
  | "schema-fields"
  | "schema-field-picker"
  | "conditions"
  | "expression"
  | "display"
  | "callout-accordion-display"
  | "tab-with-text";

export interface SelectOption {
  value: string | number | boolean;
  label: string;
  description?: string;
  disabled?: boolean;
}

export interface FieldSchema<Whole = Record<string, unknown>> {
  id: string;
  type: FormFieldType;
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
    config: {
      projectKey: string;
      workflowId: string;
      nodeId: string;
      store: WorkflowStore;
      executionMode?: number;
    },
  ) => unknown;
  onChange?: (
    value: unknown,
    data: Whole,
    config: {
      projectKey: string;
      workflowId: string;
      nodeId: string;
      store: WorkflowStore;
      executionMode?: number;
    },
  ) => Whole | void;
  options?:
    | SelectOption[]
    | ((
        data: Whole,
        config: {
          projectKey: string;
          workflowId: string;
          store: WorkflowStore;
          executionMode?: number;
        },
      ) => Promise<SelectOption[]>);
  fixedKeys?:
    | string[]
    | ((
        data: Whole,
        config: {
          projectKey: string;
          workflowId: string;
          nodeId: string;
          store: WorkflowStore;
          executionMode?: number;
        },
      ) => Promise<string[]>);
  fixedKeysDependencies?: string[];
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
  itemType?: FormFieldType;
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
  config: {
    projectKey: string;
    workflowId: string;
    nodeId: string;
    store: WorkflowStore;
    executionMode?: number;
  };
  readOnly?: boolean;
  className?: string;
  placeholder?: string;
}

export type FormField = FieldSchema;
export type FieldComponentProps<T = unknown> = FieldProps<T>;

/**
 * Value shape stored by the `conditional-multiselect` field type.
 * Combines the selected values with an AND/OR match mode so the field can
 * persist both pieces of state through a single key write.
 */
export interface ConditionalMultiselectValue {
  mode: "and" | "or";
  values: string[];
}
