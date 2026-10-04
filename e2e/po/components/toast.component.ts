import { expect, type Locator, type Page } from "@playwright/test";

/**
 * Success/error toast matcher.
 *
 * The hook is `showSuccessToast({ description: "..." })` and renders the
 * description into a Radix Toast region. Two flows can show the same
 * description in quick succession (create + deactivate), so callers
 * generally use `.first()`.
 */
export class ToastComponent {
  constructor(private readonly page: Page) {}

  /** Visible toast whose text matches the supplied description (substring match). */
  toast(description: string | RegExp): Locator {
    return this.page.getByText(description);
  }

  async expectSuccessVisible(description: string | RegExp, timeout = 15_000): Promise<void> {
    await expect(this.toast(description).first()).toBeVisible({ timeout });
  }

  async expectNotPresent(description: string | RegExp): Promise<void> {
    await expect(this.toast(description)).toHaveCount(0);
  }

  async expectAnyAlertVisible(timeout = 15_000): Promise<void> {
    await expect(
      this.page
        .getByRole("alert")
        .or(this.page.getByRole("status"))
        .or(this.page.getByText(/Error|Server error/))
        .first(),
    ).toBeVisible({ timeout });
  }
}
