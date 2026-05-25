export interface OutputSchemaField {
  key: string;
  type: "string" | "number" | "boolean" | "object" | "array";
  children?: OutputSchemaField[];
}
