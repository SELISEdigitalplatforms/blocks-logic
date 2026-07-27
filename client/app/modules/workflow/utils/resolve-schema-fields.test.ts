import { describe, it, expect, vi, beforeEach } from "vitest";
import {
  resolveSchemaFields,
  schemaFieldsToOutputSchema,
  buildEmptyFieldMapping,
} from "./resolve-schema-fields";
import type { ResolvedSchemaField } from "@blocks-workflow/types/resolved-schema-field.types";
import type { IRemoteSchemaField } from "../models/data-service";
import { dataService } from "../services/data.service";

vi.mock("../services/data.service", () => ({
  dataService: {
    getSchemaList: vi.fn(),
  },
}));

const remote = (
  name: string,
  type: string,
  isArray = false,
): IRemoteSchemaField =>
  ({ name, type, isArray, description: "" }) as IRemoteSchemaField;

const resolved = (
  name: string,
  type: string,
  isArray = false,
  fields?: ResolvedSchemaField[],
): ResolvedSchemaField =>
  ({ name, type, isArray, description: "", fields }) as ResolvedSchemaField;

describe("schemaFieldsToOutputSchema", () => {
  it("maps primitive types to output schema types", () => {
    const out = schemaFieldsToOutputSchema([
      resolved("count", "int"),
      resolved("flag", "boolean"),
      resolved("label", "string"),
    ]);
    expect(out).toEqual([
      { key: "count", type: "number" },
      { key: "flag", type: "boolean" },
      { key: "label", type: "string" },
    ]);
  });

  it("marks array fields as array and nests entity children", () => {
    const out = schemaFieldsToOutputSchema([
      resolved("tags", "string", true),
      resolved("addr", "AddressDto", false, [resolved("city", "string")]),
    ]);
    expect(out[0]).toEqual({ key: "tags", type: "array" });
    expect(out[1].key).toBe("addr");
    expect(out[1].type).toBe("object");
    expect(out[1].children).toEqual([{ key: "city", type: "string" }]);
  });
});

describe("buildEmptyFieldMapping", () => {
  it("produces empty strings for scalar fields", () => {
    expect(buildEmptyFieldMapping([resolved("a", "string"), resolved("b", "int")])).toEqual(
      { a: "", b: "" },
    );
  });

  it("uses dot notation for nested non-array entities", () => {
    const mapping = buildEmptyFieldMapping([
      resolved("addr", "AddressDto", false, [resolved("city", "string")]),
    ]);
    expect(mapping).toEqual({ "addr.city": "" });
  });

  it("uses index 0 for array entities", () => {
    const mapping = buildEmptyFieldMapping([
      resolved("items", "ItemDto", true, [resolved("sku", "string")]),
    ]);
    expect(mapping).toEqual({ "items.0.sku": "" });
  });
});

describe("resolveSchemaFields", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("expands entity types against the fetched DTO map", async () => {
    vi.mocked(dataService.getSchemaList).mockResolvedValue({
      data: {
        items: [
          { schemaName: "AddressDto", fields: [remote("city", "string")] },
        ],
      },
    } as never);

    const result = await resolveSchemaFields(
      [remote("address", "AddressDto")],
      "pk",
    );

    expect(result).toHaveLength(1);
    expect(result[0].name).toBe("address");
    expect(result[0].fields).toEqual([
      expect.objectContaining({ name: "city", type: "string" }),
    ]);
  });

  it("leaves unknown types unexpanded", async () => {
    vi.mocked(dataService.getSchemaList).mockResolvedValue({
      data: { items: [] },
    } as never);

    const result = await resolveSchemaFields([remote("id", "string")], "pk");

    expect(result[0].fields).toBeUndefined();
  });

  it("returns fields even when the schema list request fails", async () => {
    vi.mocked(dataService.getSchemaList).mockRejectedValue(new Error("network"));

    const result = await resolveSchemaFields([remote("id", "string")], "pk");

    expect(result).toEqual([
      expect.objectContaining({ name: "id", type: "string" }),
    ]);
  });
});
