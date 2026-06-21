import { ResolvedSchemaField } from "@blocks-workflow/types/resolved-schema-field.types";
import { OutputSchemaField } from "@blocks-workflow/types/output-schema.types";
import { dataService } from "../services/data.service";
import { IRemoteSchemaField } from "../models/data-service";

const MAX_DEPTH = 10;

/**
 * Resolves schema fields recursively, expanding entity types (DTOs) into nested fields.
 * Fetches all DTO schemas once, then resolves each field's type against the DTO map.
 */
export async function resolveSchemaFields(
  fields: IRemoteSchemaField[],
  projectKey: string,
): Promise<ResolvedSchemaField[]> {
  // Fetch all DTO schemas to build a name lookup map
  const dtoSchemas = await dataService
    .getSchemaList({
      projectKey,
      pageNo: 1,
      pageSize: 200,
      sortDescending: true,
      sortBy: "CreatedDate",
      keyword: "",
      schemaType: 2,
    })
    .then((res) => res.data.items ?? [])
    .catch(() => []);

  const schemaByName = new Map<string, IRemoteSchemaField[]>();
  dtoSchemas.forEach((item) => {
    if (item?.schemaName) {
      schemaByName.set(item.schemaName.trim(), item.fields ?? []);
    }
  });

  return resolveFields(fields, schemaByName, new Set(), 0);
}

function resolveFields(
  fields: IRemoteSchemaField[],
  schemaByName: Map<string, IRemoteSchemaField[]>,
  visited: Set<string>,
  depth: number,
): ResolvedSchemaField[] {
  if (depth > MAX_DEPTH) return [];

  return fields.map((field) => {
    const typeName = field.type?.trim() ?? "";
    const resolved: ResolvedSchemaField = {
      name: field.name,
      type: typeName,
      isArray: field.isArray,
      description: field.description,
    };

    // Check if this type is a known DTO (entity) and resolve its sub-fields
    const dtoFields = schemaByName.get(typeName);
    if (dtoFields && !visited.has(typeName)) {
      const nextVisited = new Set(visited);
      nextVisited.add(typeName);
      resolved.fields = resolveFields(
        dtoFields,
        schemaByName,
        nextVisited,
        depth + 1,
      );
    }

    return resolved;
  });
}

/**
 * Maps a resolved schema type string to an OutputSchemaField type.
 */
function mapResolvedType(
  type: string,
  isArray: boolean,
  hasChildren: boolean,
): OutputSchemaField["type"] {
  if (isArray) return "array";
  if (hasChildren) return "object";
  const t = type.toLowerCase();
  if (
    t.includes("int") ||
    t.includes("double") ||
    t.includes("decimal") ||
    t.includes("number") ||
    t.includes("float")
  )
    return "number";
  if (t.includes("bool")) return "boolean";
  if (t.includes("object") || t.includes("document")) return "object";
  if (t.includes("array")) return "array";
  return "string";
}

/**
 * Converts a ResolvedSchemaField[] tree into OutputSchemaField[] (with children) for output schema.
 */
export function schemaFieldsToOutputSchema(
  fields: ResolvedSchemaField[],
): OutputSchemaField[] {
  return fields.map((f) => {
    const hasChildren = !!(f.fields && f.fields.length > 0);
    const output: OutputSchemaField = {
      key: f.name,
      type: mapResolvedType(f.type, f.isArray, hasChildren),
    };
    if (hasChildren) {
      output.children = schemaFieldsToOutputSchema(f.fields!);
    }
    return output;
  });
}

/**
 * Builds a flat dot-path field mapping from a schema tree.
 * Scalars get "fieldName": "", nested entities use dot notation,
 * arrays include index: "arrayField.0.child": "".
 */
export function buildEmptyFieldMapping(
  fields: ResolvedSchemaField[],
  prefix = "",
): Record<string, string> {
  const result: Record<string, string> = {};
  for (const f of fields) {
    const path = prefix ? `${prefix}.${f.name}` : f.name;
    if (f.fields && f.fields.length > 0) {
      if (f.isArray) {
        // Start with one item at index 0
        Object.assign(result, buildEmptyFieldMapping(f.fields, `${path}.0`));
      } else {
        Object.assign(result, buildEmptyFieldMapping(f.fields, path));
      }
    } else {
      result[path] = "";
    }
  }
  return result;
}
