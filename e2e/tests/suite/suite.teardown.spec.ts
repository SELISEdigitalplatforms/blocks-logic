import { test } from "@playwright/test";
import { deleteCreatedProject } from "../../support/create-and-delete-project";
import { ensureAuthenticated } from "../../support/login-helper";
import {
  clearLogicProject,
  clearLogicSession,
  readLogicProject,
} from "../../support/logic-project";
import { shouldDeleteSharedProject } from "../../support/run-outcome";

test.describe("logic suite teardown", () => {
  test("delete shared project when all suite tests passed", async ({ page }) => {
    test.setTimeout(120_000);

    const fixture = readLogicProject();
    if (!fixture) return;

    if (!shouldDeleteSharedProject()) {
      console.log(
        `[e2e] Keeping project "${fixture.projectName}" on the console ` +
          "(a test failed or E2E_KEEP_PROJECT=1).",
      );
      return;
    }

    await ensureAuthenticated(page);
    const deleted = await deleteCreatedProject(page, fixture.projectName, {
      itemId: fixture.itemId,
    });

    clearLogicProject();
    clearLogicSession();

    if (!deleted) {
      console.log(
        `[e2e] Project "${fixture.projectName}" was not deleted automatically — ` +
          "remove it manually from the console if needed.",
      );
    }
  });
});
