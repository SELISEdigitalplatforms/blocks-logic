import { test, expect } from "@playwright/test";
import fs from "fs";
import path from "path";
import { reuseOrCreateSharedProject } from "../../support/create-and-delete-project";
import { dismissSingleSessionTakeover, loginThroughOidc } from "../../support/login-helper";
import { LOGIC_SESSION_PATH, writeLogicProject } from "../../support/logic-project";
import { e2eBaseUrl } from "../../support/env";
import { resetRunOutcome } from "../../support/run-outcome";

async function waitForDashboardChrome(page: import("@playwright/test").Page) {
  const workflow = page.getByRole("link", { name: "Workflow" });
  for (let attempt = 0; attempt < 6; attempt++) {
    await dismissSingleSessionTakeover(page);
    if (await workflow.isVisible({ timeout: 10_000 }).catch(() => false)) {
      return;
    }
    await page.reload({ waitUntil: "domcontentloaded" }).catch(() => {});
    await page.waitForTimeout(1_000);
  }
  await expect(workflow).toBeVisible({ timeout: 30_000 });
}

test.describe("logic suite setup", () => {
  test("login, reuse or create one shared project", async ({ page }) => {
    test.setTimeout(300_000);
    resetRunOutcome();

    await loginThroughOidc(page);
    await dismissSingleSessionTakeover(page);
    await expect(
      page.getByRole("heading", { name: /Your Blocks Projects|Welcome to SELISE Blocks/ }),
    ).toBeVisible({ timeout: 30_000 });

    const configuredId = process.env.E2E_PROJECT_ID?.trim();
    const reuseName = process.env.E2E_REUSE_PROJECT_NAME?.trim();

    let projectName: string;
    let dashboardUrl: string;
    let itemId: string;

    if (configuredId) {
      dashboardUrl = `${e2eBaseUrl()}/app/${configuredId}/dashboard`;
      await page.goto(dashboardUrl, { waitUntil: "domcontentloaded" });
      await waitForDashboardChrome(page);
      projectName =
        reuseName ||
        (
          await page
            .getByRole("button", { name: /^Project / })
            .innerText()
            .catch(() => "Project")
        ).replace(/^Project\s+/i, "").trim();
      itemId = configuredId;
    } else {
      ({ projectName, dashboardUrl, itemId } = await reuseOrCreateSharedProject(page));
      await waitForDashboardChrome(page);
    }

    if (!itemId) {
      throw new Error(`Could not resolve itemId from dashboard URL: ${dashboardUrl}`);
    }

    writeLogicProject({
      projectName,
      itemId,
      dashboardUrl: dashboardUrl.replace(/\?.*/, ""),
    });

    // Persist AFTER the shared project is open so localStorage keeps the selected
    // project/environment. Saving only post-login makes /app/{id}/dashboard bounce
    // back to /app/console in feature tests.
    fs.mkdirSync(path.dirname(LOGIC_SESSION_PATH), { recursive: true });
    await page.context().storageState({ path: LOGIC_SESSION_PATH });
  });
});
