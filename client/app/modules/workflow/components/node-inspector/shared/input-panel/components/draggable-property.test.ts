import { describe, expect, it } from "vitest";
import { buildOutputExpression } from "./draggable-property";

describe("buildOutputExpression", () => {
  it("indexes an array property without a dot before the bracket", () => {
    expect(buildOutputExpression("ids[0]", "HTTP Request", true)).toBe("{{$json.output.ids[0]}}");
  });

  it("indexes a root array with no dot after output", () => {
    expect(buildOutputExpression("[0]", "HTTP Request", true)).toBe("{{$json.output[0]}}");
  });

  it("keeps a field path under an indexed element", () => {
    expect(buildOutputExpression("items[0].name", "HTTP Request", true)).toBe(
      "{{$json.output.items[0].name}}",
    );
  });

  it("uses the node reference when there is more than one predecessor", () => {
    expect(buildOutputExpression("ids[0]", "HTTP Request", false)).toBe(
      '{{$node["HTTP Request"].json.output.ids[0]}}',
    );
  });
});
