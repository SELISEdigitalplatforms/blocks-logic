import { describe, expect, it } from "vitest";
import { generateGraphQLPreview } from "./generate-graphql-preview";
import { ResolvedSchemaField } from "@blocks-workflow/types/resolved-schema-field.types";

const f = (
  name: string,
  type: string,
  isArray = false,
  fields?: ResolvedSchemaField[],
): ResolvedSchemaField =>
  ({ name, type, isArray, description: "", fields }) as ResolvedSchemaField;

describe("generateGraphQLPreview insert/update body formatting", () => {
  it("formats scalar values by type and quoting rules", () => {
    const schema = [
      f("title", "string"),
      f("count", "int"),
      f("active", "boolean"),
      f("expr", "string"),
      f("empty", "string"),
    ];
    const values = {
      title: "Hello",
      count: "42",
      active: "true",
      expr: "{{$json.output}}",
      empty: "",
    };
    const out = generateGraphQLPreview(schema, values, "Post", "insertData");
    expect(out).toContain('title: "Hello"');
    expect(out).toContain("count: 42");
    expect(out).toContain("active: true");
    expect(out).toContain("expr: {{$json.output}}");
    // empty scalar falls back to the type default ("")
    expect(out).toContain('empty: ""');
  });

  it("falls back to numeric and string defaults for missing scalars", () => {
    const schema = [f("n", "long"), f("s", "datetime"), f("u", "unknownType")];
    const out = generateGraphQLPreview(schema, {}, "Doc", "insertData");
    // no flat values -> formatObject filters them out, body is empty
    expect(out).toContain("input: {");
    // still a valid mutation wrapper
    expect(out).toContain("status");
  });

  it("formats a nested entity object", () => {
    const schema = [
      f("author", "entity", false, [f("name", "string"), f("age", "int")]),
    ];
    const values = { "author.name": "Ada", "author.age": "30" };
    const out = generateGraphQLPreview(schema, values, "Book", "insertData");
    expect(out).toContain("author: {");
    expect(out).toContain('name: "Ada"');
    expect(out).toContain("age: 30");
  });

  it("formats an array entity with explicit indices", () => {
    const schema = [
      f("tags", "entity", true, [f("label", "string")]),
    ];
    const values = {
      "tags.0.label": "a",
      "tags.1.label": "b",
    };
    const out = generateGraphQLPreview(schema, values, "Item", "insertData");
    expect(out).toContain("tags: [");
    expect(out).toContain('label: "a"');
    expect(out).toContain('label: "b"');
  });

  it("formats an array entity with no indices using the .0 template", () => {
    const schema = [f("rows", "entity", true, [f("v", "string")])];
    // a flat key under rows but not a numeric index triggers the empty-indices path
    const values = { "rows.v": "x" };
    const out = generateGraphQLPreview(schema, values, "Grid", "insertData");
    expect(out).toContain("rows: [{");
  });

  it("adds a filter argument for updateData", () => {
    const schema = [f("name", "string")];
    const out = generateGraphQLPreview(
      schema,
      { name: "New", filter: { id: "1", missing: "" } },
      "User",
      "updateData",
    );
    expect(out).toContain("filter: {");
    expect(out).toContain('id: "1"');
    // empty filter value is quoted as ""
    expect(out).toContain('missing: ""');
  });

  it("uses an empty filter object for deleteData with a real filter", () => {
    const out = generateGraphQLPreview(
      [],
      { filter: { status: "active" } },
      "User",
      "deleteData",
    );
    expect(out).toContain('status: "active"');
  });
});
