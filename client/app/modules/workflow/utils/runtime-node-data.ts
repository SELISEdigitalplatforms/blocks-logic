import { ExecutedItem } from "@blocks-workflow/models/workflow.model";
import { OutputSchemaField } from "@blocks-workflow/types/output-schema.types";

type NodeDataKey = "Input" | "Output";

type SchemaType = OutputSchemaField["type"];

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === "object" && !Array.isArray(value);
}

function inferType(value: unknown): SchemaType | null {
  if (value === null || value === undefined) return null;
  if (Array.isArray(value)) return "array";
  if (typeof value === "number") return "number";
  if (typeof value === "boolean") return "boolean";
  if (typeof value === "object") return "object";
  return "string";
}

function mergeType(current: SchemaType | undefined, next: SchemaType): SchemaType {
  if (!current || current === next) return next;

  // Mixed primitive/object values are represented as string for a stable single-type schema.
  if ((current === "array" && next === "object") || (current === "object" && next === "array")) {
    return "object";
  }

  return "string";
}

export function getOrderedNodeData(
  items: ExecutedItem[],
  nodeId: string,
  dataKey: NodeDataKey,
): unknown[] {
  return items
    .filter((item) => item.nodeId === nodeId)
    .sort((a, b) => a.itemIndex - b.itemIndex)
    .map((item) => item.data?.[dataKey]);
}

export function inferSchemaFromRuntimeRows(rows: unknown[]): OutputSchemaField[] {
  const keysInOrder: string[] = [];
  const keySet = new Set<string>();
  const typeByKey: Record<string, SchemaType> = {};

  for (const row of rows) {
    if (!isRecord(row)) continue;

    for (const key of Object.keys(row)) {
      if (!keySet.has(key)) {
        keySet.add(key);
        keysInOrder.push(key);
      }

      const inferred = inferType(row[key]);
      if (!inferred) continue;

      typeByKey[key] = mergeType(typeByKey[key], inferred);
    }
  }

  return keysInOrder.map((key) => ({
    key,
    type: typeByKey[key] ?? "string",
  }));
}
