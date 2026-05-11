import { ResolvedSchemaField } from "@blocks-workflow/types/resolved-schema-field.types";

const PRIMITIVE_DEFAULTS: Record<string, string> = {
  string: '""',
  int: "0",
  long: "0",
  float: "0",
  double: "0",
  decimal: "0",
  number: "0",
  boolean: "false",
  datetime: '""',
  date: '""',
};

function getDefaultValue(type: string): string {
  const t = type.toLowerCase();
  for (const [key, val] of Object.entries(PRIMITIVE_DEFAULTS)) {
    if (t.includes(key)) return val;
  }
  return '""';
}

function formatValue(value: unknown, schema: ResolvedSchemaField, indent: string, flatValues: Record<string, unknown>, prefix: string): string {
  // If the field is an entity with sub-fields
  if (schema.fields && schema.fields.length > 0) {
    if (schema.isArray) {
      // Determine array indices from flat keys
      const indices = getArrayIndicesFromFlat(flatValues, prefix);
      if (indices.length === 0) {
        const inner = formatObject(schema.fields, flatValues, indent + "  ", prefix + ".0");
        return `[{\n${inner}\n${indent}}]`;
      }
      const formatted = indices.map((idx) => {
        const inner = formatObject(schema.fields!, flatValues, indent + "  ", `${prefix}.${idx}`);
        return `{\n${inner}\n${indent}}`;
      });
      return `[${formatted.join(", ")}]`;
    }
    const inner = formatObject(schema.fields, flatValues, indent + "  ", prefix);
    return `{\n${inner}\n${indent}}`;
  }

  // Scalar value — read directly from flat map
  const flatVal = flatValues[prefix];
  if (flatVal !== undefined && flatVal !== null && flatVal !== "") {
    const str = String(flatVal);
    if (str.startsWith("{{") || str === "true" || str === "false" || !isNaN(Number(str))) {
      return str;
    }
    return `"${str}"`;
  }

  return getDefaultValue(schema.type);
}

function getArrayIndicesFromFlat(flatValues: Record<string, unknown>, prefix: string): number[] {
  const indices = new Set<number>();
  const pfx = prefix + ".";
  for (const key of Object.keys(flatValues)) {
    if (key.startsWith(pfx)) {
      const rest = key.slice(pfx.length);
      const idx = parseInt(rest.split(".")[0], 10);
      if (!isNaN(idx)) indices.add(idx);
    }
  }
  return Array.from(indices).sort((a, b) => a - b);
}

function formatObject(
  fields: ResolvedSchemaField[],
  flatValues: Record<string, unknown>,
  indent: string,
  prefix: string,
): string {
  return fields
    .filter((f) => {
      const childPrefix = prefix ? `${prefix}.${f.name}` : f.name;
      const isEntity = !!(f.fields && f.fields.length > 0);
      if (isEntity) {
        const pfx = childPrefix + ".";
        return Object.keys(flatValues).some((k) => k.startsWith(pfx));
      }
      return childPrefix in flatValues;
    })
    .map((f) => {
      const childPrefix = prefix ? `${prefix}.${f.name}` : f.name;
      const formatted = formatValue(flatValues[childPrefix], f, indent, flatValues, childPrefix);
      return `${indent}${f.name}: ${formatted}`;
    })
    .join("\n");
}

type ActionType = "getData" | "insertData" | "updateData" | "deleteData";

const ACTION_PREFIX: Record<ActionType, string> = {
  getData: "get",
  insertData: "insert",
  updateData: "update",
  deleteData: "delete",
};

const ACTION_OPERATION: Record<ActionType, "query" | "mutation"> = {
  getData: "query",
  insertData: "mutation",
  updateData: "mutation",
  deleteData: "mutation",
};

/**
 * Generate a GraphQL operation string from schema fields and current values.
 */
export function generateGraphQLPreview(
  schemaFields: ResolvedSchemaField[],
  values: Record<string, unknown>,
  schemaName: string,
  actionType: string,
): string {
  const action = (actionType || "getData") as ActionType;
  const prefix = ACTION_PREFIX[action] ?? "get";
  const operation = ACTION_OPERATION[action] ?? "query";
  const operationName = `${prefix}${schemaName}`;

  if (action === "getData") {
    const selectionFields = buildSelectionFields(schemaFields, "    ");
    return [
      `${operation} {`,
      `  ${operationName} {`,
      selectionFields,
      "  }",
      "}",
    ].join("\n");
  }

  if (action === "deleteData") {
    const filter = (values as Record<string, unknown>)?.filter;
    const filterStr = filter && typeof filter === "object"
      ? formatFilterArg(filter as Record<string, unknown>, "    ")
      : "{}";
    return [
      `${operation} {`,
      `  ${operationName}(`,
      `    filter: ${filterStr}`,
      "  ) {",
      "    status",
      "    itemId",
      "  }",
      "}",
    ].join("\n");
  }

  // insertData / updateData
  const body = formatObject(schemaFields, values, "      ", "");
  const lines = [`${operation} {`, `  ${operationName}(`, `    input: {`];
  if (body) {
    lines.push(body);
  }
  lines.push("    }");

  if (action === "updateData") {
    const filter = (values as Record<string, unknown>)?.filter;
    const filterStr = filter && typeof filter === "object"
      ? formatFilterArg(filter as Record<string, unknown>, "    ")
      : "{}";
    lines.push(`    filter: ${filterStr}`);
  }

  lines.push("  ) {", "    status", "    itemId", "  }", "}");
  return lines.join("\n");
}

function buildSelectionFields(fields: ResolvedSchemaField[], indent: string): string {
  return fields
    .map((f) => {
      if (f.fields && f.fields.length > 0) {
        const nested = buildSelectionFields(f.fields, indent + "  ");
        return `${indent}${f.name} {\n${nested}\n${indent}}`;
      }
      return `${indent}${f.name}`;
    })
    .join("\n");
}

function formatFilterArg(filter: Record<string, unknown>, indent: string): string {
  const entries = Object.entries(filter);
  if (entries.length === 0) return "{}";
  const inner = entries
    .map(([k, v]) => {
      const val = v !== undefined && v !== null && v !== "" ? `"${String(v)}"` : '""';
      return `${indent}  ${k}: ${val}`;
    })
    .join("\n");
  return `{\n${inner}\n${indent}}`;
}
