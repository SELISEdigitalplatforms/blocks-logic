import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { SandboxRulesCard } from "./sandbox-rules-card";
import { SANDBOX_CODE_RULES } from "../../constants/limits.constant";

describe("SandboxRulesCard", () => {
  it("is open by default — reuse is always on — and says what breaking a rule does", () => {
    renderWithProviders(<SandboxRulesCard />);
    expect(screen.getByTestId("sandbox-rules-mode").textContent).toMatch(/Calls of a deployed function reuse the sandbox/);
    expect(screen.getByTestId("sandbox-rules-mode").textContent).toMatch(/Sandbox replaced/);
    expect(screen.getAllByText("Don't:")).toHaveLength(SANDBOX_CODE_RULES.length);
    expect(screen.getAllByText("Do:")).toHaveLength(SANDBOX_CODE_RULES.length);
  });

  it("can be folded away", async () => {
    renderWithProviders(<SandboxRulesCard />);
    await userEvent.click(screen.getByText("What not to do, and what to do instead"));
    expect(screen.queryByTestId("sandbox-rules")).toBeNull();
  });

  it("tells people to use ctx.waitUntil directly — it exists in every mode now", () => {
    const waitUntilRule = SANDBOX_CODE_RULES.find((r) => r.fixCode.includes("ctx.waitUntil"));
    expect(waitUntilRule?.fixCode).toBe("ctx.waitUntil(saveAudit(entry));");
  });
});
