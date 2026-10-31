import test, { expect } from "@playwright/test";
import { openSharedProjectDashboard } from "../../support/suite-helpers";
import { readLogicProject } from "../../support/logic-project";
import { EventTriggersPage, SidebarComponent, ToastComponent } from "../../po";

/**
 * flow: Event Triggers page (blocks-logic#375, SPEC3 §10).
 *
 * Needs the shared project seeded with the Blocks Event Trigger catalog (output/seed/BlocksEventTriggerSeed).
 * On a project without it, only the not-seeded alert is checked.
 *
 * Covered here (one admin identity, the shared project):
 *   1. No sidebar item (H16); the page loads by URL; every catalog row has its switches (H6); search narrows (H9)
 *   2. A Workflow toggle persists across a reload (H7), then is put back
 *  11. Two concurrent saves on different events both persist (C10), then are put back
 *
 * Not automated (SPEC3 §10 says they are run by hand, or they need fixtures this suite does not have):
 *   3, 6, 7  the node select, confirm dialog and publish rejection need published workflows on a seeded event
 *   4, 5, 10 need a real producer (blocks-iam) and the broker
 *   8        needs a second identity with workflow::read only
 *   9        needs a tenant without the catalog
 */

const EVENT_LABEL = "User changed";
const SECOND_LABEL = "SMS status";

test.describe("flow: Event Triggers page", () => {
  test("Event Triggers — no menu item, list, search, toggle persists, concurrent saves", async ({ page }) => {
    test.setTimeout(180_000);
    const itemId = readLogicProject()?.itemId;
    test.skip(!itemId, "needs the shared logic project fixture");

    const sidebar = new SidebarComponent(page);
    const triggers = new EventTriggersPage(page);
    const toast = new ToastComponent(page);

    await openSharedProjectDashboard(page);

    await test.step("H16: the sidebar has no Event Triggers item", async () => {
      await expect(sidebar.getLink("Schedules")).toBeVisible({ timeout: 30_000 });
      await expect(page.getByRole("link", { name: /event triggers/i })).toHaveCount(0);
    });

    await triggers.goto(itemId!);
    await triggers.expectLoaded();

    if (!(await triggers.isSeeded())) {
      await test.step("C12: an unseeded project shows the not-seeded alert and no switches", async () => {
        await expect(triggers.notSeededAlert).toBeVisible();
        await expect(page.getByRole("switch")).toHaveCount(0);
      });
      return;
    }

    await test.step("H6: every catalog event has a Workflow and a Function switch", async () => {
      const rows = await triggers.workflowSwitches.count();
      expect(rows).toBeGreaterThan(0);
      await expect(page.getByRole("switch", { name: /for functions$/ })).toHaveCount(rows);
    });

    await test.step("H9: searching 'UserMutation' leaves one row", async () => {
      await triggers.search.fill("UserMutation");
      await expect(triggers.workflowSwitches).toHaveCount(1);
      await triggers.search.fill("");
    });

    const original = (await triggers.workflowSwitch(EVENT_LABEL).getAttribute("aria-checked")) === "true";

    await test.step("H7: a Workflow toggle saves, toasts and persists across a reload", async () => {
      await triggers.workflowSwitch(EVENT_LABEL).click();
      await toast.expectSuccessVisible(new RegExp(`${EVENT_LABEL} (enabled|disabled) for workflows`));
      await expect(triggers.workflowSwitch(EVENT_LABEL)).toHaveAttribute("aria-checked", String(!original));
      await page.reload({ waitUntil: "domcontentloaded" });
      await triggers.expectLoaded();
      await expect(triggers.workflowSwitch(EVENT_LABEL)).toHaveAttribute("aria-checked", String(!original), {
        timeout: 30_000,
      });
    });

    await test.step("C10: two saves on different events at once both persist", async () => {
      const second = (await triggers.functionSwitch(SECOND_LABEL).getAttribute("aria-checked")) === "true";
      const firstFn = (await triggers.functionSwitch(EVENT_LABEL).getAttribute("aria-checked")) === "true";
      await Promise.all([triggers.functionSwitch(EVENT_LABEL).click(), triggers.functionSwitch(SECOND_LABEL).click()]);
      await page.reload({ waitUntil: "domcontentloaded" });
      await triggers.expectLoaded();
      await expect(triggers.functionSwitch(EVENT_LABEL)).toHaveAttribute("aria-checked", String(!firstFn), { timeout: 30_000 });
      await expect(triggers.functionSwitch(SECOND_LABEL)).toHaveAttribute("aria-checked", String(!second));

      // Put both back.
      await triggers.functionSwitch(EVENT_LABEL).click();
      await expect(triggers.functionSwitch(EVENT_LABEL)).toHaveAttribute("aria-checked", String(firstFn));
      await triggers.functionSwitch(SECOND_LABEL).click();
      await expect(triggers.functionSwitch(SECOND_LABEL)).toHaveAttribute("aria-checked", String(second));
    });

    await test.step("cleanup: the Workflow flag is back to its original value", async () => {
      if ((await triggers.workflowSwitch(EVENT_LABEL).getAttribute("aria-checked")) !== String(original)) {
        await triggers.workflowSwitch(EVENT_LABEL).click();
        // Turning it off may ask first when published workflows use it.
        const disable = page.getByRole("button", { name: "Disable" });
        if (await disable.isVisible().catch(() => false)) await disable.click();
      }
      await expect(triggers.workflowSwitch(EVENT_LABEL)).toHaveAttribute("aria-checked", String(original));
    });
  });
});
