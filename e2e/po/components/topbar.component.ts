import { expect, type Locator, type Page } from "@playwright/test";

/**
 * Topbar controls on the project dashboard / console:
 *   - Theme switcher ("Change theme" dropdown: Auto / Light / Dark)
 *   - Language selector
 *   - Notification bell
 *   - Apps switcher
 *   - User avatar menu
 *
 * Radix popovers/menus used here re-render on each animation tick, so some
 * helpers expose a `force: true` flag for actions that are otherwise flaky.
 */
export class TopbarComponent {
  constructor(private readonly page: Page) {}

  // ---- Theme switcher ----

  /**
   * Theme switcher trigger. Since genesis-os 4.3.3 (pinned on dev by 50d21caf) the
   * topbar renders a "Change theme" dropdown with an "Appearance" radio group
   * (Auto / Light / Dark) instead of the old Dark/Light tablist.
   */
  get themeSwitcher(): Locator {
    return this.page.getByRole("button", { name: "Change theme" });
  }

  themeOption(label: "Auto" | "Light" | "Dark"): Locator {
    return this.page.getByRole("menuitemradio", { name: label });
  }

  get darkOption(): Locator {
    return this.themeOption("Dark");
  }

  get lightOption(): Locator {
    return this.themeOption("Light");
  }

  async expectThemeApplied(theme: "dark" | "light"): Promise<void> {
    if (theme === "dark") {
      await expect(this.page.locator("html")).toHaveClass(/dark/);
    } else {
      await expect(this.page.locator("html")).not.toHaveClass(/dark/);
    }
  }

  private async selectTheme(option: Locator): Promise<void> {
    await this.themeSwitcher.click();
    await expect(option).toBeVisible();
    await option.click();
    await expect(option).toBeHidden();
  }

  async switchToDark(): Promise<void> {
    await this.selectTheme(this.darkOption);
  }

  async switchToLight(): Promise<void> {
    await this.selectTheme(this.lightOption);
  }

  // ---- Language selector ----

  get languageButton(): Locator {
    return this.page.getByRole("button", { name: /^en$/i });
  }

  async openLanguageMenu(): Promise<void> {
    await this.languageButton.click();
  }

  menuItem(name: string | RegExp): Locator {
    return this.page.getByRole("menuitem", { name });
  }

  async expectMenuItemDisabled(name: string | RegExp): Promise<void> {
    await expect(this.menuItem(name)).toHaveAttribute("aria-disabled", "true");
  }

  // ---- Notifications ----

  get notificationBell(): Locator {
    return this.page.getByTestId("notification-bell");
  }

  get notificationsHeading(): Locator {
    return this.page.getByText("Notifications", { exact: true });
  }

  get markAllAsReadButton(): Locator {
    return this.page.getByRole("button", { name: "Mark all as read" });
  }

  async openNotifications(): Promise<void> {
    await this.notificationBell.click();
    await expect(this.notificationsHeading).toBeVisible({ timeout: 10_000 });
  }

  async clickMarkAllAsRead(): Promise<void> {
    await this.markAllAsReadButton.click({ force: true, timeout: 10_000 });
  }

  async expectNotificationsClosed(): Promise<void> {
    await expect(this.notificationsHeading).toHaveCount(0);
  }

  // ---- Apps switcher ----

  get appsButton(): Locator {
    return this.page.getByRole("button", { name: "SELISE Blocks apps" });
  }

  get appsHeading(): Locator {
    return this.page.getByText("SELISE Blocks", { exact: true });
  }

  async openAppsMenu(): Promise<void> {
    await this.appsButton.click();
    await expect(this.appsHeading).toBeVisible();
  }

  async expectAppsMenuClosed(): Promise<void> {
    await expect(this.appsHeading).toHaveCount(0);
  }

  /** Outside-click at top-left to close Radix popovers whose trigger is intercepted on re-click. */
  async clickOutside(): Promise<void> {
    await this.page.mouse.click(20, 20);
  }

  // ---- User menu ----

  get userMenuButton(): Locator {
    return this.page.getByRole("button", { name: "Open user menu" });
  }

  async openUserMenu(): Promise<void> {
    await this.userMenuButton.click();
  }

  get myProfileMenuItem(): Locator {
    return this.page.getByRole("menuitem", { name: "My Profile" });
  }

  get logOutMenuItem(): Locator {
    return this.page.getByRole("menuitem", { name: "Log out" });
  }

  async clickMyProfile(): Promise<void> {
    await this.myProfileMenuItem.click();
  }

  // ---- Apps menu items ----------------------------------------------------

  /**
   * Apps menu opens as a Radix popover listing the SELISE Blocks apps.
   * Items use "button" or "menuitem" semantics depending on the trigger.
   * This matcher covers both, and the caller decides which one to assert.
   */
  appMenuItem(name: string | RegExp): Locator {
    return this.page.getByRole("menuitem", { name }).or(
      this.page.getByRole("link", { name }),
    );
  }

  async clickAppMenuItem(name: string | RegExp): Promise<void> {
    await this.appMenuItem(name).first().click();
  }

  // ---- Log out navigation -------------------------------------------------

  async logOut(): Promise<void> {
    await this.openUserMenu();
    await this.logOutMenuItem.click();
  }
}
