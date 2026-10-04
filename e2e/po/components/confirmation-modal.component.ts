import { expect, type Locator, type Page } from "@playwright/test";

/**
 * Shared ConfirmationModal used by:
 *   - Delete Schedule (title: "Delete Schedule", subtitle: "Are you sure…")
 *   - Delete Workflow (title: "Delete Workflow", subtitle: "Are you sure…")
 *   - Create-project OS wizard (title: "Delete this environment?")
 *
 * The underlying ConfirmationModal renders:
 *   <DialogTitle>…</DialogTitle>
 *   <DialogDescription>…</DialogDescription>
 *   <Button>Cancel</Button>   (default unless caller overrides)
 *   <Button>Yes</Button>      (default unless caller overrides)
 */
export class ConfirmationModalComponent {
  constructor(private readonly page: Page) {}

  /** Dialog <h2> by accessible name (e.g. "Delete Schedule"). */
  title(title: string): Locator {
    return this.page.getByRole("heading", { name: title });
  }

  /** Dialog subtitle text (e.g. "Are you sure you want to delete this schedule?"). */
  subtitle(text: string): Locator {
    return this.page.getByText(text);
  }

  get cancelButton(): Locator {
    return this.page.getByRole("button", { name: "Cancel" }).first();
  }

  get confirmButton(): Locator {
    return this.page.getByRole("button", { name: "Yes" }).first();
  }

  async expectOpen(title: string, subtitleText?: string): Promise<void> {
    await expect(this.title(title)).toBeVisible({ timeout: 10_000 });
    if (subtitleText) {
      await expect(this.subtitle(subtitleText)).toBeVisible();
    }
  }

  async expectClosed(title: string, timeout = 5_000): Promise<void> {
    await expect(this.title(title)).not.toBeVisible({ timeout });
  }

  async clickCancel(): Promise<void> {
    await this.cancelButton.click({ force: true });
  }

  async clickConfirm(): Promise<void> {
    await this.confirmButton.click();
  }
}
