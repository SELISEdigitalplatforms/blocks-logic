import { describe, expect, it } from "vitest";
import { getErrorMessage, handleErrorMessages, isErrorWithErrors } from "./error";

describe("getErrorMessage", () => {
  it("returns a default when there are no errors", () => {
    expect(getErrorMessage({})).toBe("Something went wrong.");
  });

  it("prefers a mapped message when a key matches", () => {
    expect(
      getErrorMessage({ email: "raw" }, { email: "Email is invalid" }),
    ).toEqual(["Email is invalid"]);
  });

  it("collects string and array error values", () => {
    expect(
      getErrorMessage({ a: "one", b: ["two", "three"] }),
    ).toEqual(["one", "two, three"]);
  });

  it("ignores empty arrays and returns the default when nothing collected", () => {
    expect(getErrorMessage({ a: [] })).toBe("Something went wrong.");
  });
});

describe("isErrorWithErrors", () => {
  it("is true when the object carries an errors object", () => {
    expect(isErrorWithErrors({ errors: { a: "x" } })).toBe(true);
  });

  it("is false for non-objects and objects without errors", () => {
    expect(isErrorWithErrors(null)).toBe(false);
    expect(isErrorWithErrors("nope")).toBe(false);
    expect(isErrorWithErrors({ other: 1 })).toBe(false);
  });
});

describe("handleErrorMessages", () => {
  it("returns a string error as-is", () => {
    expect(handleErrorMessages("bad")).toBe("bad");
  });

  it("delegates to getErrorMessage for error objects", () => {
    expect(handleErrorMessages({ a: "x" })).toEqual(["x"]);
  });

  it("returns a generic message for arrays and unexpected values", () => {
    expect(handleErrorMessages([1, 2])).toBe("An unexpected error occurred.");
    expect(handleErrorMessages(42)).toBe("An unexpected error occurred.");
  });
});
