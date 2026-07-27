import { describe, it, expect } from "vitest";
import { generateGraphQLPreview } from "./generate-graphql-preview";
import type { ResolvedSchemaField } from "@blocks-workflow/types/resolved-schema-field.types";

const field = (
  name: string,
  type: string,
  isArray = false,
  fields?: ResolvedSchemaField[],
): ResolvedSchemaField =>
  ({ name, type, isArray, description: "", fields }) as ResolvedSchemaField;

describe("generateGraphQLPreview", () => {
  it("builds a query with a selection set for getData", () => {
    const schema = [field("id", "string"), field("name", "string")];
    const result = generateGraphQLPreview(schema, {}, "User", "getData");

    expect(result).toContain("query {");
    expect(result).toContain("getUser {");
    expect(result).toContain("id");
    expect(result).toContain("name");
  });

  it("nests selection fields for entity sub-fields in getData", () => {
    const schema = [
      field("address", "AddressDto", false, [field("city", "string")]),
    ];
    const result = generateGraphQLPreview(schema, {}, "User", "getData");

    expect(result).toContain("address {");
    expect(result).toContain("city");
  });

  it("builds an insert mutation with an input object", () => {
    const schema = [field("name", "string")];
    const result = generateGraphQLPreview(
      schema,
      { name: "John" },
      "User",
      "insertData",
    );

    expect(result).toContain("mutation {");
    expect(result).toContain("insertUser(");
    expect(result).toContain("input:");
    expect(result).toContain('name: "John"');
  });

  it("builds an update mutation including a filter argument", () => {
    const schema = [field("name", "string")];
    const result = generateGraphQLPreview(
      schema,
      { name: "Jane", filter: { itemId: "abc" } },
      "User",
      "updateData",
    );

    expect(result).toContain("updateUser(");
    expect(result).toContain("filter:");
    expect(result).toContain('itemId: "abc"');
  });

  it("builds a delete mutation with a filter and status/itemId selection", () => {
    const result = generateGraphQLPreview(
      [],
      { filter: { itemId: "abc" } },
      "User",
      "deleteData",
    );

    expect(result).toContain("deleteUser(");
    expect(result).toContain("filter:");
    expect(result).toContain("status");
    expect(result).toContain("itemId");
  });

  it("defaults an empty action to a getData query", () => {
    const schema = [field("id", "string")];
    const result = generateGraphQLPreview(schema, {}, "User", "");

    expect(result).toContain("query {");
    expect(result).toContain("getUser {");
  });

  it("emits an empty filter object when none is provided for deleteData", () => {
    const result = generateGraphQLPreview([], {}, "User", "deleteData");
    expect(result).toContain("filter: {}");
  });
});
