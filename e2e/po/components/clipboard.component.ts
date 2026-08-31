import type { Page } from "@playwright/test";

/**
 * Clipboard helper. Centralizes:
 *   - granting clipboard-read / clipboard-write permissions for the context
 *   - reading the current clipboard text via the page's own navigator API
 *
 * Playwright cannot read the OS clipboard directly — the page must read
 * it via `navigator.clipboard.readText()` and return the value to the
 * test runner. Granting permissions first is required for that call to
 * resolve outside of a user-gesture context.
 */
export class ClipboardComponent {
  constructor(private readonly page: Page) {}

  async grantPermissions(): Promise<void> {
    await this.page.context().grantPermissions(["clipboard-read", "clipboard-write"]);
  }

  async readText(): Promise<string> {
    return this.page.evaluate(() => navigator.clipboard.readText());
  }

  async writeText(text: string): Promise<void> {
    await this.page.evaluate((value) => navigator.clipboard.writeText(value), text);
  }
}
