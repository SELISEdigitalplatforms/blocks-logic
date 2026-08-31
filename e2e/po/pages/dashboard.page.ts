import { expect, type Locator, type Page } from "@playwright/test";
import { e2eBaseUrl } from "../../support/env";
import { ClipboardComponent } from "../components/clipboard.component";

/**
 * Project dashboard `/app/:itemId/dashboard`.
 *
 * Sections under test:
 *   - "Project Details" card (Name, X-Blocks-Key, Environment)
 *   - "Core APIs" card (grouped endpoint lists, "Copy as cURL" hover-reveal)
 */
export class DashboardPage {
  constructor(private readonly page: Page) {}

  // ---- Navigation ----------------------------------------------------------

  async gotoDashboard(itemId: string): Promise<void> {
    await this.page.goto(`${e2eBaseUrl()}/app/${itemId}/dashboard`, {
      waitUntil: "domcontentloaded",
    });
  }

  get projectDetailsHeading(): Locator {
    return this.page.getByRole("heading", { name: "Project Details" });
  }

  async expectProjectDetailsHeading(timeout = 30_000): Promise<void> {
    await expect(this.projectDetailsHeading).toBeVisible({ timeout });
  }

  get main(): Locator {
    return this.page.getByRole("main");
  }

  // ---- Project Details card ------------------------------------------------

  get nameLabel(): Locator {
    return this.main.getByText("Name", { exact: true });
  }

  get xBlocksKeyLabel(): Locator {
    return this.main.getByText("X-Blocks-Key", { exact: true });
  }

  get environmentLabel(): Locator {
    return this.main.getByText("Environment", { exact: true });
  }

  environmentButton(): Locator {
    return this.main.getByRole("button", {
      name: /^(Production|Development|Testing|Staging|IAT)$/,
    });
  }

  async expectProjectDetailsCardVisible(): Promise<void> {
    await expect(this.nameLabel).toBeVisible();
    await expect(this.xBlocksKeyLabel).toBeVisible();
    await expect(this.environmentLabel).toBeVisible();
    await expect(this.environmentButton()).toBeVisible();
  }

  // ---- X-Blocks-Key (masked + hover-reveal copy) ---------------------------

  /**
   * The X-Blocks-Key label and its row share a parent — `getByText("X-Blocks-Key").locator("..")`
   * walks up one level.
   */
  get xBlocksKeyRow(): Locator {
    return this.xBlocksKeyLabel.locator("..");
  }

  get xBlocksKeyCopyButton(): Locator {
    return this.xBlocksKeyRow.getByRole("button");
  }

  async expectXBlocksKeyMasked(): Promise<void> {
    await expect(this.xBlocksKeyRow).toContainText("*");
  }

  /**
   * Click the masked key's copy button. The button is wrapped in a parent
   * that gates its visibility with `opacity-0 group-hover:opacity-100`, and
   * the genesis-os copy component renders `aria-label` toggling between
   * "Copy" and "Copied!" rather than an icon swap. We therefore:
   *
   *   1. Invoke the button's native `click()` method via `evaluate` — this
   *      bypasses the opacity-0 pointer-event blackhole and React's
   *      synthetic-event delegation picks it up automatically.
   *   2. Wait for either the aria-label flip ("Copied!") or the lucide
   *      Check icon to appear, whichever the rendered component uses.
   *   3. Verify the clipboard received the key value — the source of truth.
   */
  async copyXBlocksKey(): Promise<void> {
    const clipboard = new ClipboardComponent(this.page);
    await clipboard.grantPermissions();
    await this.xBlocksKeyRow.hover();
    await this.xBlocksKeyCopyButton.evaluate((el) => {
      (el as HTMLButtonElement).click();
    });
    await expect
      .poll(
        async () => {
          const aria = await this.xBlocksKeyCopyButton.getAttribute("aria-label");
          const ariaOk = aria === "Copied!";
          const iconOk = await this.xBlocksKeyCopyButton
            .locator("svg.lucide.lucide-check")
            .isVisible()
            .catch(() => false);
          return ariaOk || iconOk;
        },
        { timeout: 10_000, intervals: [200] },
      )
      .toBe(true);
    const text = await clipboard.readText();
    expect(text.length).toBeGreaterThan(0);
  }

  // ---- Core APIs card ------------------------------------------------------

  get coreApisHeading(): Locator {
    return this.page.getByRole("heading", { name: "Core APIs" });
  }

  get coreApisDescription(): Locator {
    return this.page.getByText("Available endpoints for this module");
  }

  get endpointsCountText(): Locator {
    return this.page.getByText(/^\d+ Endpoints?$/);
  }

  async expectCoreApisCardVisible(timeout = 30_000): Promise<void> {
    await expect(this.coreApisHeading).toBeVisible({ timeout });
    await expect(this.coreApisDescription).toBeVisible();
    await expect(this.endpointsCountText).toBeVisible();
  }

  /** Collapsed endpoint group buttons — name format is "GroupName N". */
  endpointGroupButtons(): Locator {
    return this.page.getByRole("button", { name: /^[A-Za-z]+\s+\d+$/ });
  }

  async expectEndpointsGroupCount(greaterThan = 0): Promise<void> {
    const count = await this.endpointGroupButtons().count();
    expect(count).toBeGreaterThan(greaterThan);
  }

  async expectFirstGroupCollapsed(timeout = 15_000): Promise<void> {
    const first = this.endpointGroupButtons().first();
    await expect(first).toBeVisible({ timeout });
    await expect(first).toHaveAttribute("aria-expanded", "false");
  }

  /** Click the first endpoint group, retrying while Radix re-renders. */
  async expandFirstEndpointGroup(): Promise<void> {
    const first = this.endpointGroupButtons().first();
    for (let attempt = 0; attempt < 5; attempt++) {
      try {
        if ((await first.getAttribute("aria-expanded")) === "true") return;
        await first.scrollIntoViewIfNeeded();
        await first.click({ timeout: 10_000 });
      } catch {
        // retry — Radix re-renders the button on each animation tick
      }
    }
    await expect(first).toHaveAttribute("aria-expanded", "true", { timeout: 15_000 });
  }

  // ---- Copy as cURL --------------------------------------------------------

  get copyAsCurlRow(): Locator {
    return this.page.getByText("Copy as cURL").first().locator("..");
  }

  get copyAsCurlButton(): Locator {
    return this.copyAsCurlRow.getByRole("button");
  }

  async copyAsCurl(): Promise<string> {
    const clipboard = new ClipboardComponent(this.page);
    await clipboard.grantPermissions();
    await this.copyAsCurlRow.hover();
    await this.copyAsCurlButton.scrollIntoViewIfNeeded();
    // Native click() — see copyXBlocksKey() for the React-delegation +
    // opacity-0 rationale.
    await this.copyAsCurlButton.evaluate((el) => {
      (el as HTMLButtonElement).click();
    });
    await expect
      .poll(
        async () => {
          const aria = await this.copyAsCurlButton.getAttribute("aria-label");
          const ariaOk = aria === "Copied!";
          const iconOk = await this.copyAsCurlButton
            .locator("svg.lucide.lucide-check")
            .isVisible()
            .catch(() => false);
          return ariaOk || iconOk;
        },
        { timeout: 10_000, intervals: [200] },
      )
      .toBe(true);
    const text = await clipboard.readText();
    expect(text.length).toBeGreaterThan(0);
    expect(text).toContain("curl");
    return text;
  }
}
