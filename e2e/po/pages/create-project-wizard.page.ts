import { expect, type Locator, type Page } from "@playwright/test";

/**
 * Cross-app project creation wizard hosted by Blocks OS.
 *
 * Flow (4 steps):
 *   1. "Name your project" → fill name + check agreements + Continue
 *   2. "Add resource"      → Continue (skip)
 *   3. "Select environments" → click Development + Submit
 *   4. "Your project has been created." toast
 *
 * After success the wizard stays on OS; callers return to the Logic
 * console themselves.
 */
export class CreateProjectWizard {
  constructor(private readonly page: Page) {}

  // ---- Step 1: Name + agreements -------------------------------------------

  get nameHeading(): Locator {
    return this.page.getByRole("heading", { name: "Name your project" });
  }

  /** Visible project-name input. The OS wizard also has a hidden one. */
  get nameInput(): Locator {
    return this.page.locator('[placeholder="Enter your project name"]:visible');
  }

  get termsCheckbox(): Locator {
    return this.page.getByRole("checkbox", { name: "I accept the Terms of services" });
  }

  get confirmCheckbox(): Locator {
    return this.page.getByRole("checkbox", { name: "I confirm that I will use" });
  }

  get continueButton(): Locator {
    return this.page.getByRole("button", { name: "Continue", exact: true });
  }

  async expectOnNameStep(timeout = 30_000): Promise<void> {
    await expect(this.nameHeading).toBeVisible({ timeout });
  }

  async fillName(name: string): Promise<void> {
    await this.nameInput.fill(name);
  }

  async acceptAgreements(): Promise<void> {
    await this.confirmCheckbox.click();
    await this.termsCheckbox.click();
  }

  async expectContinueEnabled(): Promise<void> {
    await expect(this.continueButton).toBeEnabled();
  }

  async clickContinue(): Promise<void> {
    await this.continueButton.click();
  }

  // ---- Step 2: Add resource ------------------------------------------------

  get resourceHeading(): Locator {
    return this.page.getByRole("heading", { name: "Add resource" });
  }

  async expectOnResourceStep(timeout = 30_000): Promise<void> {
    await expect(this.resourceHeading).toBeVisible({ timeout });
  }

  async skipResources(): Promise<void> {
    await this.continueButton.click();
  }

  // ---- Step 3: Select environments -----------------------------------------

  get environmentsHeading(): Locator {
    return this.page
      .getByText("Select environments", { exact: true })
      .and(this.page.locator(":visible"));
  }

  environmentChip(name: string): Locator {
    return this.page.getByText(name, { exact: true }).and(this.page.locator(":visible"));
  }

  get submitButton(): Locator {
    return this.page.getByRole("button", { name: "Submit" });
  }

  async expectOnEnvironmentsStep(timeout = 30_000): Promise<void> {
    await expect(this.environmentsHeading).toBeVisible({ timeout });
  }

  async selectEnvironment(name: string): Promise<void> {
    await this.environmentChip(name).click();
  }

  async expectSubmitEnabled(): Promise<void> {
    await expect(this.submitButton).toBeEnabled();
  }

  async clickSubmit(): Promise<void> {
    await this.submitButton.click();
  }

  // ---- Step 4: Success -----------------------------------------------------

  get successText(): Locator {
    return this.page.getByText("Your project has been created.", { exact: true });
  }

  async expectCreateSuccess(timeout = 30_000): Promise<void> {
    await expect(this.successText).toBeVisible({ timeout });
    await expect(this.page).toHaveURL(/\/app\/(console|project\/[^/]+\/environments)\/?$/, {
      timeout: 20_000,
    });
  }
}
