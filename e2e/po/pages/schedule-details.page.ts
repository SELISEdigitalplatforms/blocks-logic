import { expect, type Locator, type Page } from "@playwright/test";
import { e2eBaseUrl } from "../../support/env";
import { ClipboardComponent } from "../components/clipboard.component";

/**
 * Schedule details page `/app/:itemId/schedule/:scheduleId`.
 *
 * Layout:
 *   - Header (name + Active/Inactive badge + Status switch + Edit + Delete)
 *   - Tabs: Overview (default) | Executions ("Coming Soon")
 *   - Overview cards:
 *     - Timing & Schedule  (cron + cron preset badge + Start/End date fallback)
 *     - Payload            (Monaco + Copy Payload)
 *     - Webhook Configuration
 *       - Endpoint URL (method badge + URL + copy)
 *       - Signing Secret (masked bullets by default; eye reveal; copy icon swap)
 *       - Custom Headers  (or "No custom headers configured.")
 */
export class ScheduleDetailsPage {
  constructor(private readonly page: Page) {}

  async gotoDetails(itemId: string, scheduleId: string): Promise<void> {
    await this.page.goto(`${e2eBaseUrl()}/app/${itemId}/schedule/${scheduleId}`, {
      waitUntil: "domcontentloaded",
    });
  }

  async expectHeading(name: string, exact = false, timeout = 30_000): Promise<void> {
    await expect(this.page.getByRole("heading", { name, exact })).toBeVisible({ timeout });
  }

  // ---- Header --------------------------------------------------------------

  get activeBadge(): Locator {
    return this.page.getByText("Active", { exact: true }).first();
  }

  get statusSwitch(): Locator {
    return this.page.getByRole("switch").first();
  }

  get editButton(): Locator {
    return this.page.getByRole("button", { name: "Edit" });
  }

  get deleteButton(): Locator {
    return this.page.getByRole("button", { name: "Delete" });
  }

  async expectEditEnabled(): Promise<void> {
    await expect(this.editButton).toBeEnabled();
  }

  async expectDeleteEnabled(): Promise<void> {
    await expect(this.deleteButton).toBeEnabled();
  }

  async clickEdit(): Promise<void> {
    await this.editButton.click();
  }

  async clickDelete(): Promise<void> {
    await this.deleteButton.click();
  }

  // ---- Tabs ----------------------------------------------------------------

  get overviewTab(): Locator {
    return this.page.getByRole("tab", { name: "Overview" });
  }

  get executionsTab(): Locator {
    return this.page.getByRole("tab", { name: "Executions" });
  }

  async clickOverviewTab(): Promise<void> {
    await this.overviewTab.click();
  }

  async clickExecutionsTab(): Promise<void> {
    await this.executionsTab.click();
  }

  async expectTabActive(tab: "Overview" | "Executions"): Promise<void> {
    await expect(this.page.getByRole("tab", { name: tab })).toHaveAttribute("data-state", "active");
  }

  async expectExecutionComingSoon(): Promise<void> {
    await expect(
      this.page.getByRole("heading", { name: "Execution History Coming Soon" }),
    ).toBeVisible();
  }

  // ---- Timing & Schedule card ---------------------------------------------

  get timingCardTitle(): Locator {
    return this.page.getByText("Timing & Schedule", { exact: true });
  }

  async expectCronValue(value: string): Promise<void> {
    await expect(this.page.getByText(value, { exact: false }).first()).toBeVisible();
  }

  async expectCronPresetBadge(label: string): Promise<void> {
    await expect(this.page.getByText(label, { exact: true })).toBeVisible();
  }

  get startDateFallback(): Locator {
    return this.page.getByText("Immediate (No start date)");
  }

  get endDateFallback(): Locator {
    return this.page.getByText("Indefinite (No end date)");
  }

  // ---- Webhook Configuration card ----------------------------------------

  get webhookCardTitle(): Locator {
    return this.page.getByText("Webhook Configuration", { exact: true });
  }

  get endpointUrlLabel(): Locator {
    return this.page.getByText("Endpoint URL", { exact: true });
  }

  /** Method badge — text inside <Badge className="… uppercase">. */
  methodBadge(method: string): Locator {
    return this.page.locator(`xpath=//*[normalize-space(text())='${method}']`).first();
  }

  async expectEndpointUrl(url: string): Promise<void> {
    await expect(this.page.getByText(url, { exact: true })).toBeVisible();
  }

  // ---- Signing Secret (masked → reveal → copy) ----------------------------

  get signingSecretLabel(): Locator {
    return this.page.getByText("Signing Secret", { exact: true });
  }

  /** Masked bullets — `'•'+` matches one or more • characters. */
  get maskedSecretText(): Locator {
    return this.page.getByText(/•+/);
  }

  /**
   * The eye toggle is the first <button> after the "Signing Secret" label;
   * the copy button is the next sibling. Both are lucide ghost icon
   * buttons with no accessible name, so we anchor on the label.
   */
  get eyeToggle(): Locator {
    return this.signingSecretLabel.locator("xpath=following::button[1]").first();
  }

  get copySecretButton(): Locator {
    return this.eyeToggle.locator("xpath=following::button[1]");
  }

  async expectSigningSecretMasked(): Promise<void> {
    await expect(this.signingSecretLabel).toBeVisible();
    await expect(this.maskedSecretText).toBeVisible();
  }

  async revealSigningSecret(plaintext: string): Promise<void> {
    await this.eyeToggle.click();
    await expect(this.page.getByText(plaintext, { exact: true })).toBeVisible();
    await expect(this.eyeToggle.locator("svg.lucide.lucide-eye-off")).toBeVisible();
  }

  /**
   * Click copy and assert on the icon swap (no "Copied" text rendered for
   * the Signing Secret — see schedule-details.tsx lines 380–395).
   * lucide-react renders `<svg class="lucide lucide-{name}">`.
   */
  async copySigningSecret(expectedPlaintext: string): Promise<void> {
    const clipboard = new ClipboardComponent(this.page);
    await clipboard.grantPermissions();
    await this.copySecretButton.click();
    await expect(this.copySecretButton.locator("svg.lucide.lucide-check")).toBeVisible({
      timeout: 10_000,
    });
    const text = await clipboard.readText();
    expect(text).toBe(expectedPlaintext);
  }

  // ---- Custom Headers ------------------------------------------------------

  get emptyCustomHeadersText(): Locator {
    return this.page.getByText("No custom headers configured.");
  }

  async expectEmptyCustomHeaders(): Promise<void> {
    await expect(this.emptyCustomHeadersText).toBeVisible();
  }
}
