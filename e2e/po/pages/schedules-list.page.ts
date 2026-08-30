import { expect, type Locator, type Page } from "@playwright/test";
import { e2eBaseUrl } from "../../support/env";

/**
 * Schedule list page `/app/:itemId/schedule`.
 *
 * Renders either:
 *   - empty state (`ScheduleEmptyState`) with "Create your first schedule"
 *     heading + "Create schedule" button
 *   - populated TanStack table with Name / Cron Expression / Start date /
 *     End date / Status columns and a per-row action menu (Open / Edit /
 *     Delete). The Delete menu item opens the shared DeleteScheduleDialog.
 */
export class SchedulesListPage {
  constructor(private readonly page: Page) {}

  async gotoList(itemId: string): Promise<void> {
    await this.page.goto(`${e2eBaseUrl()}/app/${itemId}/schedule`, {
      waitUntil: "domcontentloaded",
    });
  }

  get heading(): Locator {
    return this.page.getByRole("heading", { name: "Schedules", exact: true });
  }

  async expectHeadingVisible(timeout = 30_000): Promise<void> {
    await expect(this.heading).toBeVisible({ timeout });
  }

  // ---- Empty state ---------------------------------------------------------

  get emptyHeading(): Locator {
    return this.page.getByRole("heading", { name: "Create your first schedule" });
  }

  get createScheduleEmptyButton(): Locator {
    return this.page.getByRole("button", { name: "Create schedule" });
  }

  /** Populated state's "Add Schedule" button in the toolbar. */
  get addScheduleButton(): Locator {
    return this.page.getByRole("button", { name: "Add Schedule" }).first();
  }

  async isEmptyState(): Promise<boolean> {
    return this.emptyHeading.isVisible({ timeout: 5_000 }).catch(() => false);
  }

  async expectEmptyState(): Promise<void> {
    await expect(this.emptyHeading).toBeVisible();
    await expect(this.createScheduleEmptyButton).toBeVisible();
  }

  // ---- Column headers (populated state only) ------------------------------

  async expectColumnHeaders(): Promise<void> {
    await expect(this.page.getByRole("columnheader", { name: "Name" })).toBeVisible();
    await expect(this.page.getByRole("columnheader", { name: "Cron Expression" })).toBeVisible();
    await expect(this.page.getByRole("columnheader", { name: "Start date" })).toBeVisible();
    await expect(this.page.getByRole("columnheader", { name: "End date" })).toBeVisible();
    await expect(this.page.getByRole("columnheader", { name: "Status" })).toBeVisible();
  }

  // ---- Search --------------------------------------------------------------

  get searchInput(): Locator {
    return this.page.getByPlaceholder("Search...").first();
  }

  /** Fill search input (force, since the clear-X icon covers part of the click target). */
  async searchBy(text: string): Promise<void> {
    await this.searchInput.fill(text, { force: true });
  }

  async clearSearch(): Promise<void> {
    await this.searchInput.fill("", { force: true });
  }

  // ---- Pagination ----------------------------------------------------------

  /** Rows-per-page Select — Radix SelectTrigger has role="combobox". */
  get pageSizeSelect(): Locator {
    return this.page.getByRole("combobox").first();
  }

  async setPageSize(size: number): Promise<void> {
    await this.pageSizeSelect.click({ force: true });
    await this.page.getByRole("option", { name: String(size) }).click({ force: true });
  }

  get pageIndicator(): Locator {
    return this.page.getByText(/Page 1 of/);
  }

  // ---- Row actions (Open / Edit / Delete) ----------------------------------

  get firstDataRow(): Locator {
    return this.page.getByRole("row").nth(1);
  }

  /** Trigger button (EllipsisVertical icon) inside a row. */
  rowMenuTrigger(row: Locator): Locator {
    return row.locator("button").first();
  }

  async openFirstRowMenu(): Promise<void> {
    await this.rowMenuTrigger(this.firstDataRow).click({ force: true });
  }

  get rowMenuItem(): (name: string) => Locator {
    return (name) => this.page.getByRole("menuitem", { name });
  }

  /** Click a menu item by its visible label inside the open row menu. */
  async clickRowMenuItem(name: string): Promise<void> {
    await this.page.getByRole("menuitem", { name }).click({ force: true });
  }

  /** Read the name cell of the first data row. */
  async firstRowName(): Promise<string> {
    return (await this.firstDataRow.locator("td").first().innerText()).trim();
  }

  async hasFirstRow(): Promise<boolean> {
    return this.firstDataRow.isVisible({ timeout: 5_000 }).catch(() => false);
  }

  async expectFirstRowContains(text: string): Promise<void> {
    await expect(this.firstDataRow).toContainText(text, { ignoreCase: true });
  }
}
