import { describe, it, expect } from "vitest";
import { addCopySuffix } from "./add-copy-suffix.util";

describe("addCopySuffix", () => {
  it("should append (Copy 1) to a plain name", () => {
    expect(addCopySuffix("My Workflow")).toBe("My Workflow (Copy 1)");
  });

  it("should increment (Copy 1) to (Copy 2)", () => {
    expect(addCopySuffix("My Workflow (Copy 1)")).toBe("My Workflow (Copy 2)");
  });

  it("should increment (Copy 2) to (Copy 3)", () => {
    expect(addCopySuffix("My Workflow (Copy 2)")).toBe("My Workflow (Copy 3)");
  });

  it("should increment a higher copy number correctly", () => {
    expect(addCopySuffix("My Workflow (Copy 9)")).toBe("My Workflow (Copy 10)");
  });

  it("should handle names with no trailing space before (Copy N)", () => {
    expect(addCopySuffix("Workflow(Copy 3)")).toBe("Workflow (Copy 4)");
  });

  it("should append (Copy 1) to a single-word name", () => {
    expect(addCopySuffix("Workflow")).toBe("Workflow (Copy 1)");
  });

  it("should handle names that already contain parentheses elsewhere", () => {
    const result = addCopySuffix("My (Special) Workflow");
    expect(result).toBe("My (Special) Workflow (Copy 1)");
  });

  it("should trim whitespace from base name when incrementing", () => {
    // 'My Workflow  (Copy 1)' — extra trailing space before (Copy)
    const result = addCopySuffix("My Workflow  (Copy 1)");
    expect(result).toBe("My Workflow (Copy 2)");
  });
});
