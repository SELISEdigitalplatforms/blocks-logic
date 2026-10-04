import { expect, type Locator, type Page } from "@playwright/test";

/**
 * Sidebar nav + workspace widgets (Project/Environment).
 *
 * Used by every spec that runs against a project dashboard. Methods that
 * need an exact `name` take it as an argument so this class does not bake
 * in the assumption that "Overview" is always the right link.
 */
export class SidebarComponent {
  constructor(private readonly page: Page) {}

  /** Any sidebar link by accessible name (e.g. "Overview", "Workflow", "Schedules"). */
  getLink(name: string | RegExp): Locator {
    return this.page.getByRole("link", { name }).first();
  }

  /** Assert that a sidebar link's href matches the given pattern. */
  async expectLinkHref(name: string | RegExp, pattern: RegExp): Promise<void> {
    const href = await this.getLink(name).getAttribute("href");
    expect(href ?? "").toMatch(pattern);
  }

  /** Click a sidebar link by accessible name. */
  async clickLink(name: string | RegExp): Promise<void> {
    await this.getLink(name).click();
  }

  // ---- Workspace widgets (sidebar section header + Project/Environment chips) ----

  /** "Workspace" paragraph in the desktop sidebar's section header. */
  get workspaceLabel(): Locator {
    return this.page.getByText("Workspace", { exact: true });
  }

  /** Sidebar container that wraps the Workspace label and its widgets. */
  get workspaceContainer(): Locator {
    return this.workspaceLabel.locator("xpath=ancestor::div[1]");
  }

  /** Disabled "Project …" widget in the sidebar. */
  get projectWidget(): Locator {
    return this.workspaceContainer.getByRole("button", { name: /^Project/ });
  }

  /** Disabled "Environment …" widget in the sidebar. */
  get environmentWidget(): Locator {
    return this.workspaceContainer.getByRole("button", { name: /^Environment/ });
  }

  async expectWorkspaceWidgetsDisabled(): Promise<void> {
    await expect(this.projectWidget).toBeVisible();
    await expect(this.environmentWidget).toBeVisible();
    await expect(this.projectWidget).toBeDisabled();
    await expect(this.environmentWidget).toBeDisabled();
  }

  async expectWorkspaceWidgetShowsProject(projectName: string): Promise<void> {
    await expect(this.projectWidget).toContainText(projectName);
  }
}
