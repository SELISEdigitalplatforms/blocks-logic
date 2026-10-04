import { expect, type Locator, type Page } from "@playwright/test";
import { e2eBaseUrl, e2eCredentials } from "../../support/env";
import { TopbarComponent } from "../components/topbar.component";

/**
 * Login flow (dev-iam OIDC) + logout.
 *
 * Note: the full OIDC redirect dance (`loginThroughOidc`) lives in
 * `e2e/support/login-helper.ts` because it is shared with suite setup.
 * This POM only exposes the form-level primitives that tests want to drive
 * directly, plus a thin wrapper that calls into the helper.
 */
export class LoginPage {
  constructor(private readonly page: Page) {}

  // ---- Product login gate ---------------------------------------------------

  /** "Log in to your account" button on the product landing page. */
  get logInButton(): Locator {
    return this.page.getByRole("button", { name: "Log in to your account" });
  }

  // ---- OIDC credential form -------------------------------------------------

  /** Email field on the dev-iam login form. */
  get emailField(): Locator {
    return this.page
      .locator("#oidc-email")
      .or(this.page.getByRole("textbox", { name: "Work Email" }));
  }

  /** Password field on the dev-iam login form. */
  get passwordField(): Locator {
    return this.page
      .locator("#oidc-password")
      .or(this.page.getByRole("textbox", { name: "Password" }));
  }

  get submitButton(): Locator {
    return this.page.getByRole("button", { name: "Login", exact: true });
  }

  async fillEmail(email: string): Promise<void> {
    await this.emailField.fill(email);
  }

  async fillPassword(password: string): Promise<void> {
    await this.passwordField.fill(password);
  }

  /** Fill both fields with the values from `e2e/.env.e2e` and submit. */
  async submitEnvCredentials(): Promise<void> {
    const { email, password } = e2eCredentials();
    await this.fillEmail(email);
    await this.fillPassword(password);
    await this.submitButton.click();
  }

  // ---- Console heading matcher ---------------------------------------------

  /** Console heading — shared between Logic and OS after login lands. */
  get consoleHeading(): Locator {
    return this.page.getByRole("heading", {
      name: /Your Blocks Projects|Welcome to SELISE Blocks/,
    });
  }

  async expectConsoleHeadingVisible(timeout = 20_000): Promise<void> {
    await expect(this.consoleHeading).toBeVisible({ timeout });
  }

  // ---- Logout (via Topbar user menu) ---------------------------------------

  get loggedOutHeading(): Locator {
    return this.page.getByRole("heading", { name: "blocks Logic" });
  }

  async expectLoggedOutHeadingVisible(timeout = 30_000): Promise<void> {
    await expect(this.loggedOutHeading).toBeVisible({ timeout });
  }

  async logOut(): Promise<void> {
    const topbar = new TopbarComponent(this.page);
    await topbar.userMenuButton.click();
    await this.page.getByText("Log out").click();
  }

  // ---- High-level navigation ------------------------------------------------

  /** Convenience: goto the product login page. */
  async gotoLogin(): Promise<void> {
    await this.page.goto(`${e2eBaseUrl()}/login`, { waitUntil: "domcontentloaded" });
  }
}
