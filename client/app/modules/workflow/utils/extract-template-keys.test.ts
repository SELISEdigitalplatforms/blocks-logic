import { describe, it, expect } from "vitest";
import { extractTemplateBodyKeys } from "./extract-template-keys";

describe("extractTemplateBodyKeys", () => {
  it("returns an empty array for undefined or empty input", () => {
    expect(extractTemplateBodyKeys()).toEqual([]);
    expect(extractTemplateBodyKeys("")).toEqual([]);
  });

  it("extracts a single templated key", () => {
    expect(extractTemplateBodyKeys("Hello {{ name }}")).toEqual(["name"]);
  });

  it("trims whitespace inside the token", () => {
    expect(extractTemplateBodyKeys("{{   user.email   }}")).toEqual(["user.email"]);
  });

  it("extracts multiple distinct keys preserving first-seen order", () => {
    expect(extractTemplateBodyKeys("{{a}} then {{b}} then {{c}}")).toEqual([
      "a",
      "b",
      "c",
    ]);
  });

  it("deduplicates repeated keys", () => {
    expect(extractTemplateBodyKeys("{{a}} and {{a}} again")).toEqual(["a"]);
  });

  it("ignores empty tokens", () => {
    expect(extractTemplateBodyKeys("{{  }} {{x}}")).toEqual(["x"]);
  });

  it("returns an empty array when there are no tokens", () => {
    expect(extractTemplateBodyKeys("plain text without tokens")).toEqual([]);
  });
});
