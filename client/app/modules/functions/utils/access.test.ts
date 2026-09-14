import { describe, expect, it } from "vitest";
import { describeTriggerAccess } from "./access";
import { ITriggerConfig } from "../types/function.types";

const token: ITriggerConfig = {
  httpEnabled: true,
  httpMethod: "Post",
  authMode: "Token",
  roles: [],
  permissions: [],
  roleMatch: "Any",
  permissionMatch: "Any",
  combine: "Or",
  workflowEnabled: true,
};

describe("describeTriggerAccess", () => {
  it("says what public actually requires — the URL and the project key", () => {
    expect(describeTriggerAccess({ ...token, authMode: "Public" })).toMatch(/project key/);
  });

  it("says so when a token trigger has no rules", () => {
    expect(describeTriggerAccess(token)).toMatch(/no extra restriction/i);
  });

  it("reads one list on its own, with the list's own any/all", () => {
    expect(describeTriggerAccess({ ...token, roles: ["admin"] })).toBe(
      "Callers must hold the role admin.",
    );
    expect(
      describeTriggerAccess({ ...token, permissions: ["a", "b"], permissionMatch: "All" }),
    ).toBe("Callers must hold all of the permissions a, b.");
  });

  it("joins the two lists with the combine, which is what the backend enforces", () => {
    // This sentence used to claim OR while the server required both — the combine is now real.
    const both = { ...token, roles: ["admin"], permissions: ["orders.write"] };
    expect(describeTriggerAccess(both)).toBe(
      "Callers must hold the role admin OR the permission orders.write.",
    );
    expect(describeTriggerAccess({ ...both, combine: "And" })).toBe(
      "Callers must hold the role admin AND the permission orders.write.",
    );
  });
});
