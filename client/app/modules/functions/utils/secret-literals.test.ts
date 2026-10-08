import { describe, it, expect } from "vitest";
import { hasTypedValue, isCredentialName, isTypedCredential } from "./secret-literals";

// Mirrors FunctionSaveSecretLiteralTests.What_counts_as_a_literal on the host.
describe("secret-literals", () => {
  it.each([
    [null, false],
    ["", false],
    ["   ", false],
    ["{{secret.abc-1}}", false],
    ["Bearer {{secret.abc-1}}", false],
    ["basic {{secret.abc-1}}", false],
    ["Bearer", false],
    ["plain", true],
    ["Bearer abc123", true],
    ["sk_{{secret.abc-1}}", true],
    ["{{secret.}}", true],
  ])("hasTypedValue(%s) is %s", (value, expected) => {
    expect(hasTypedValue(value)).toBe(expected);
  });

  it.each([
    ["STRIPE_API_KEY", true],
    ["Authorization", true],
    ["DB_PASSWORD", true],
    ["x-session-id", true],
    ["sig", true],
    ["REGION", false],
    ["Content-Type", false],
    ["", false],
  ])("isCredentialName(%s) is %s", (name, expected) => {
    expect(isCredentialName(name)).toBe(expected);
  });

  it("needs both a credential name and a typed value", () => {
    expect(isTypedCredential("API_KEY", "abc")).toBe(true);
    expect(isTypedCredential("API_KEY", "{{secret.s1}}")).toBe(false);
    expect(isTypedCredential("REGION", "abc")).toBe(false);
  });
});
