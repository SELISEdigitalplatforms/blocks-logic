import { describe, expect, it } from "vitest";
import { buildFieldReference, type FieldReference, type FieldReferenceTarget } from "./field-reference.util";

const ref = (overrides: Partial<FieldReference> = {}): FieldReference => ({
  segments: ["ItemId"],
  nodeName: "Webhook",
  isDirectParent: true,
  itemIndex: 0,
  itemIsObject: true,
  ...overrides,
});

const expression: FieldReferenceTarget = { kind: "expression" };
const each: FieldReferenceTarget = { kind: "code", mode: "each" };
const all: FieldReferenceTarget = { kind: "code", mode: "all" };

describe("buildFieldReference — expression", () => {
  it("indexes an array property without a dot before the bracket", () => {
    expect(buildFieldReference(ref({ segments: ["ids", 0] }), expression)).toBe("{{$json.output.ids[0]}}");
  });

  it("indexes a root array with no dot after output", () => {
    expect(buildFieldReference(ref({ segments: [0] }), expression)).toBe("{{$json.output[0]}}");
  });

  it("keeps a field path under an indexed element", () => {
    expect(buildFieldReference(ref({ segments: ["items", 0, "name"] }), expression)).toBe(
      "{{$json.output.items[0].name}}",
    );
  });

  it("uses the node reference when the source is not the direct parent", () => {
    expect(
      buildFieldReference(ref({ segments: ["ids", 0], nodeName: "HTTP Request", isDirectParent: false }), expression),
    ).toBe('{{$node["HTTP Request"].json.output.ids[0]}}');
  });

  it("references the whole output for an empty path", () => {
    expect(buildFieldReference(ref({ segments: [] }), expression)).toBe("{{$json.output}}");
  });

  it("ignores item index and object-ness, and leaves keys unquoted", () => {
    expect(
      buildFieldReference(ref({ segments: ["odd key"], itemIndex: 2, itemIsObject: false }), expression),
    ).toBe("{{$json.output.odd key}}");
  });
});

describe("buildFieldReference — code, each mode", () => {
  it("object item, direct parent", () => {
    expect(buildFieldReference(ref(), each)).toBe("$json.ItemId");
  });

  it("non-object item, direct parent", () => {
    expect(buildFieldReference(ref({ segments: [], itemIsObject: false }), each)).toBe("$json.json");
  });

  it("object item, earlier node", () => {
    expect(buildFieldReference(ref({ isDirectParent: false }), each)).toBe('$node["Webhook"].json.ItemId');
  });

  it("non-object item, earlier node", () => {
    expect(buildFieldReference(ref({ segments: [0], isDirectParent: false, itemIsObject: false }), each)).toBe(
      '$node["Webhook"].json.value[0]',
    );
  });

  it("ignores the item index", () => {
    expect(buildFieldReference(ref({ itemIndex: 2 }), each)).toBe("$json.ItemId");
  });

  it("renders a whole-item drag as the bare prefix", () => {
    expect(buildFieldReference(ref({ segments: [] }), each)).toBe("$json");
    expect(buildFieldReference(ref({ segments: [], isDirectParent: false }), each)).toBe('$node["Webhook"].json');
  });
});

describe("buildFieldReference — code, all mode", () => {
  it("object item, direct parent", () => {
    expect(buildFieldReference(ref({ itemIndex: 1 }), all)).toBe("$items[1].json.ItemId");
  });

  it("non-object item, direct parent", () => {
    expect(buildFieldReference(ref({ segments: [], itemIsObject: false }), all)).toBe("$items[0].json.value");
  });

  it("object item, earlier node", () => {
    expect(buildFieldReference(ref({ isDirectParent: false }), all)).toBe('$node["Webhook"].item(0).json.ItemId');
  });

  it("non-object item, earlier node", () => {
    expect(
      buildFieldReference(ref({ segments: [], isDirectParent: false, itemIndex: 2, itemIsObject: false }), all),
    ).toBe('$node["Webhook"].item(2).json.value');
  });

  it("uses the item index (0 vs 2)", () => {
    expect(buildFieldReference(ref({ itemIndex: 0 }), all)).toBe("$items[0].json.ItemId");
    expect(buildFieldReference(ref({ itemIndex: 2 }), all)).toBe("$items[2].json.ItemId");
  });
});

describe("buildFieldReference — code paths", () => {
  it("renders nested fields and array indexes", () => {
    expect(buildFieldReference(ref({ segments: ["job", "salary"] }), each)).toBe("$json.job.salary");
    expect(buildFieldReference(ref({ segments: ["tags", 0] }), each)).toBe("$json.tags[0]");
    expect(buildFieldReference(ref({ segments: ["items", 3, "name"] }), all)).toBe("$items[0].json.items[3].name");
  });

  it("quotes keys that are not identifiers", () => {
    expect(buildFieldReference(ref({ segments: ["odd key"] }), each)).toBe('$json["odd key"]');
    expect(buildFieldReference(ref({ segments: ["first-name"] }), each)).toBe('$json["first-name"]');
    expect(buildFieldReference(ref({ segments: ["0"] }), each)).toBe('$json["0"]');
    expect(buildFieldReference(ref({ segments: ['say "hi"'] }), each)).toBe('$json["say \\"hi\\""]');
    expect(buildFieldReference(ref({ segments: ["$id", "_x1"] }), each)).toBe("$json.$id._x1");
  });

  it("JSON-quotes node names", () => {
    expect(buildFieldReference(ref({ nodeName: 'My "Node"', isDirectParent: false }), each)).toBe(
      '$node["My \\"Node\\""].json.ItemId',
    );
  });
});
