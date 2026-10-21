import { describe, expect, it } from "vitest";
import { fieldsNotFound, parametersFromRows, rowsFromParameters, toFieldPath } from "./split-out";

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

describe("rows ↔ saved strings", () => {
  it("one row per field; destinations only when there is one per field", () => {
    expect(rowsFromParameters("names, scores", "name, score")).toEqual([
      { field: "names", destination: "name" },
      { field: "scores", destination: "score" },
    ]);
    expect(rowsFromParameters("lines", "")).toEqual([{ field: "lines", destination: "" }]);
    expect(rowsFromParameters("", "")).toEqual([{ field: "", destination: "" }]);
    expect(rowsFromParameters(undefined, undefined)).toEqual([{ field: "", destination: "" }]);
  });

  it("a destination equal to its field shows as empty (it was filled in for another row's name)", () => {
    expect(rowsFromParameters("a, b", "x, b")).toEqual([
      { field: "a", destination: "x" },
      { field: "b", destination: "" },
    ]);
  });

  it("saves the comma strings; empty rows are dropped", () => {
    expect(parametersFromRows([{ field: "lines", destination: "" }, { field: " ", destination: "" }])).toEqual({
      fieldToSplitOut: "lines",
      destinationFieldName: "",
    });
  });

  it("when any row has a destination, every row gets one (its own field name if empty), as the server needs", () => {
    expect(parametersFromRows([{ field: "a", destination: "x" }, { field: "b", destination: "" }])).toEqual({
      fieldToSplitOut: "a, b",
      destinationFieldName: "x, b",
    });
  });

  it("round-trips", () => {
    const saved = parametersFromRows([{ field: "names", destination: "person.name" }, { field: "scores", destination: "person.score" }]);
    expect(rowsFromParameters(saved.fieldToSplitOut, saved.destinationFieldName)).toEqual([
      { field: "names", destination: "person.name" },
      { field: "scores", destination: "person.score" },
    ]);
  });
});

describe("fieldsNotFound", () => {
  const input = [{ lines: [1], order: { lines: [2] } }, { other: 1 }];

  it("lists the fields no input item has", () => {
    expect(fieldsNotFound("lines, tags, order.lines", input, true)).toEqual(["tags"]);
  });

  it("follows dot notation, and reads dragged references as paths", () => {
    expect(fieldsNotFound("order.lines", input, false)).toEqual(["order.lines"]);
    expect(fieldsNotFound("{{$json.output.lines}}", input, true)).toEqual([]);
  });

  it("says nothing without input items", () => {
    expect(fieldsNotFound("lines", [], true)).toEqual([]);
  });
});
