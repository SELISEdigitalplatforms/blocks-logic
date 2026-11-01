import { expect, type Locator, type Page } from "@playwright/test";
import { e2eBaseUrl } from "../../support/env";

/**
 * Event Triggers page `/app/:itemId/event-triggers` (blocks-logic#375).
 *
 * No sidebar item: the Blocks Event node's "Manage events" link opens it in a new tab, or it is visited by URL.
 * Renders either the not-seeded alert or a table of catalog events with a Workflow and a Function switch per row.
 */
export class EventTriggersPage {
  constructor(private readonly page: Page) {}

  async goto(itemId: string): Promise<void> {
    await this.page.goto(`${e2eBaseUrl()}/app/${itemId}/event-triggers`, {
      waitUntil: "domcontentloaded",
    });
  }

  get heading(): Locator {
    return this.page.getByRole("heading", { name: "Event Triggers" });
  }

  get notSeededAlert(): Locator {
    return this.page.getByText("The event catalog is not set up for this project.", { exact: false });
  }

  get search(): Locator {
    return this.page.getByLabel("Search events");
  }

  /** Every Workflow switch: one per visible row. */
  get workflowSwitches(): Locator {
    return this.page.getByRole("switch", { name: /for workflows$/ });
  }

  workflowSwitch(label: string): Locator {
    return this.page.getByRole("switch", { name: `Enable ${label} for workflows` });
  }

  functionSwitch(label: string): Locator {
    return this.page.getByRole("switch", { name: `Enable ${label} for functions` });
  }

  async expectLoaded(): Promise<void> {
    await expect(this.heading).toBeVisible({ timeout: 30_000 });
  }

  async isSeeded(): Promise<boolean> {
    await expect(this.workflowSwitches.first().or(this.notSeededAlert)).toBeVisible({ timeout: 30_000 });
    return !(await this.notSeededAlert.isVisible());
  }
}
