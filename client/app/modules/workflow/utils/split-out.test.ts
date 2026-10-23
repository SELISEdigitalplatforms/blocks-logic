import { describe, expect, it } from "vitest";
import { fieldsNotFound, listToText, splitOutFields, textToList, toFieldPath } from "./split-out";

describe("toFieldPath", () => {
  it.each([
    ["{{$json.output.lines}}", "lines"],
    ["{{ $json.output.order.lines }}", "order.lines"],
    ["$json.lines", "lines"],
    ["$json.output.lines", "lines"],
    ["  lines  ", "lines"],
    ["order.lines", "order.lines"],
  ])("%s → %s", (text, path) => expect(toFieldPath(text)).toBe(path));
});

describe("splitOutFields", () => {
  it("reads the saved list and ignores anything malformed", () => {
    expect(splitOutFields([{ fieldName: "lines", outputFieldName: "line" }, null, "x", { fieldName: 3 }])).toEqual([
      { fieldName: "lines", outputFieldName: "line" },
      { fieldName: "", outputFieldName: "" },
    ]);
    expect(splitOutFields(undefined)).toEqual([]);
    expect(splitOutFields("lines")).toEqual([]);
  });
});

describe("Fields To Include text ↔ list", () => {
  it("splits on commas and trims; empty parts are dropped", () => {
    expect(textToList(" email , name,, ")).toEqual(["email", "name"]);
    expect(textToList("")).toEqual([]);
  });

  it("joins a saved list for the text box", () => {
    expect(listToText(["email", "name"])).toBe("email, name");
    expect(listToText(undefined)).toBe("");
  });
});

describe("fieldsNotFound", () => {
  const input = [{ lines: [1], order: { lines: [2] } }, { other: 1 }];
  const fields = (...names: string[]) => names.map((fieldName) => ({ fieldName, outputFieldName: "" }));

  it("lists the fields no input item has", () => {
    expect(fieldsNotFound(fields("lines", "tags", "order.lines"), input, true)).toEqual(["tags"]);
  });

  it("follows dot notation, and reads dragged references as paths", () => {
    expect(fieldsNotFound(fields("order.lines"), input, false)).toEqual(["order.lines"]);
    expect(fieldsNotFound(fields("{{$json.output.lines}}"), input, true)).toEqual([]);
  });

  it("says nothing without input items or fields", () => {
    expect(fieldsNotFound(fields("lines"), [], true)).toEqual([]);
    expect(fieldsNotFound(undefined, input, true)).toEqual([]);
  });
});
