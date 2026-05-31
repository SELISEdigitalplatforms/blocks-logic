export type FormFieldType =
  | "text"
  | "textarea"
  | "number"
  | "select"
  | "select-with-description"
  | "multiselect"
  | "checkbox"
  | "switch"
  | "radio"
  | "code-editor"
  | "key-value-pairs"
  | "array"
  | "schema-fields"
  | "schema-field-picker"
  | "conditions"
  | "expression"
  | "display";

export interface SelectOption {
  label: string;
  value: string | number | boolean;
  description?: string;
}

export interface BaseFormField {
  id: string;
  type: FormFieldType;
  label: string;
  info?: string; // Tooltip or helper text
  key: string; // Path in the config object, e.g., "parameter.httpMethod"
  required?: boolean;
  disabled?: boolean;
  placeholder?: string;
  defaultValue?: unknown;
  validation?: {
    min?: number;
    max?: number;
    pattern?: string;
    message?: string;
  };
  condition?: {
    field: string; // Field key to watch
    operator: "equals" | "notEquals" | "in" | "notIn";
    value: unknown;
  };
}

export interface TextFormField extends BaseFormField {
  type: "text" | "textarea";
  maxLength?: number;
  minLength?: number;
}

export interface NumberFormField extends BaseFormField {
  type: "number";
  min?: number;
  max?: number;
  step?: number;
}

export interface SelectFormField extends BaseFormField {
  type: "select" | "multiselect" | "radio";
  options: SelectOption[];
  searchable?: boolean;
}

export interface CheckboxFormField extends BaseFormField {
  type: "checkbox" | "switch";
}

export interface CodeEditorFormField extends BaseFormField {
  type: "code-editor";
  language?: "json" | "javascript" | "html" | "css";
  height?: number;
}

export interface KeyValuePairsFormField extends BaseFormField {
  type: "key-value-pairs";
  keyLabel?: string;
  valueLabel?: string;
  addButtonText?: string;
}

export interface ArrayFormField extends BaseFormField {
  type: "array";
  itemType: FormFieldType;
  itemOptions?: SelectOption[];
  addButtonText?: string;
}

export type FormField =
  | TextFormField
  | NumberFormField
  | SelectFormField
  | CheckboxFormField
  | CodeEditorFormField
  | KeyValuePairsFormField
  | ArrayFormField;

export interface NodeFormSchema {
  nodeType: string;
  fields: FormField[];
}
