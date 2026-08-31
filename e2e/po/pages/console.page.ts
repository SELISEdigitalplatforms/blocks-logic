import { expect, type Locator, type Page } from "@playwright/test";
import { e2eBaseUrl } from "../../support/env";

const ENV_BUTTON = /Development|Testing|Staging|IAT|UAT|Production|Pre-Prod|Prod Shadow/;

/**
 * Projects console at `/app/console`.
 *
 * Surface: project cards with env chips, "Add Project" / "Create a project"
 * CTA, Resources cards (Docs/Code/Cloud), and cross-app nav to Blocks OS
 * for project configuration.
 */
export class ConsolePage {
  constructor(private readonly page: Page) {}

  async gotoConsole(): Promise<void> {
    await this.page.goto(`${e2eBaseUrl()}/app/console`, { waitUntil: "domcontentloaded" });
  }

  get consoleHeading(): Locator {
    return this.page.getByRole("heading", {
      name: /Your Blocks Projects|Welcome to SELISE Blocks/,
    });
  }

  async expectConsoleHeading(timeout = 20_000): Promise<void> {
    await expect(this.consoleHeading).toBeVisible({ timeout });
  }

  // ---- Empty-state ("Welcome to SELISE Blocks") ----------------------------

  get welcomeHeading(): Locator {
    return this.page.getByRole("heading", { name: "Welcome to SELISE Blocks" });
  }

  get createProjectButton(): Locator {
    return this.page.getByRole("button", { name: "Create a project" });
  }

  // ---- Populated console (project grid) ------------------------------------

  get addProjectText(): Locator {
    return this.page.getByText("Add Project", { exact: true }).first();
  }

  /** Card containing a project with the given name + an env chip. */
  projectCard(projectName: string): Locator {
    return this.page
      .locator("div")
      .filter({ has: this.page.getByText(projectName, { exact: true }) })
      .filter({ has: this.page.getByRole("button", { name: ENV_BUTTON }) })
      .last();
  }

  /** Settings (gear) icon button on a project card — scopes to <main> to avoid the topbar. */
  configureButton(): Locator {
    return this.page.getByRole("main").locator("button:has(svg.lucide-settings-2)").first();
  }

  async expectConfigureButtonVisible(timeout = 15_000): Promise<void> {
    await expect(this.configureButton()).toBeVisible({ timeout });
  }

  /** Hover the configure button and assert its tooltip text — pins the selector to behavior. */
  async expectConfigureTooltip(): Promise<void> {
    await this.configureButton().hover();
    await expect(this.page.getByRole("tooltip", { name: "Configure Project" })).toBeVisible({
      timeout: 10_000,
    });
  }

  async clickConfigure(): Promise<void> {
    await this.configureButton().click();
  }

  // ---- Resources cards (Docs / Code / Cloud) -------------------------------

  docsLink(): Locator {
    return this.page.getByRole("link", { name: "Docs", exact: false });
  }

  codeLink(): Locator {
    return this.page.getByRole("link", { name: "Code", exact: false });
  }

  cloudLink(): Locator {
    return this.page.getByRole("link", { name: "Cloud", exact: false });
  }

  /**
   * Click a Resources link, capture the popup that opens, return its URL.
   * Caller is responsible for `popup.close()` once done.
   */
  async openResourcePopup(
    link: Locator,
  ): Promise<{ popup: import("@playwright/test").Page; href: string }> {
    await expect(link).toBeVisible({ timeout: 15_000 });
    const expectedHref = await link.getAttribute("href");
    const [popup] = await Promise.all([
      this.page.context().waitForEvent("page", { timeout: 15_000 }),
      link.click(),
    ]);
    await popup.waitForLoadState("domcontentloaded", { timeout: 15_000 }).catch(() => {});
    return { popup, href: expectedHref ?? "" };
  }
}
