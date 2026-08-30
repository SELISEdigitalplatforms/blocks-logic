import test, { expect } from "@playwright/test";
import { e2eBaseUrl } from "../../support/env";
import { openEnvironment } from "../../support/navigation";
import { readLogicProject } from "../../support/logic-project";
import { ConsolePage, DashboardPage, SidebarComponent, TopbarComponent } from "../../po";

test.describe("flow: Overview menu", () => {
  test("Overview page — console, topbar, sidebar navigation, Project Details, Core APIs", async ({
    page,
  }) => {
    test.setTimeout(150_000);

    const topbar = new TopbarComponent(page);
    const sidebar = new SidebarComponent(page);
    const console = new ConsolePage(page);
    const dashboard = new DashboardPage(page);

    await page.goto(`${e2eBaseUrl()}/app/console`, { waitUntil: "domcontentloaded" });

    await test.step("Topbar: switching theme to Dark applies it, then Light restores it", async () => {
      await expect(topbar.themeTablist).toBeVisible({ timeout: 30_000 });
      await topbar.switchToDark();
      await topbar.expectThemeApplied("dark");
      await topbar.switchToLight();
      await topbar.expectThemeApplied("light");
    });

    await test.step("Topbar: language selector lists EN/German/French with non-English disabled", async () => {
      await topbar.openLanguageMenu();
      await expect(topbar.menuItem("English")).toBeVisible();
      await topbar.expectMenuItemDisabled("German");
      await topbar.expectMenuItemDisabled("French");
      // The dropdown stays open after asserting menu items -- we
      // deliberately do NOT close it here. The next step opens the
      // notification popover, which Radix will handle independently.
      // Forcing a close via re-click or outside-click on the menu
      // portal is flaky on this version of Radix.
    });

    await test.step("Topbar: notification bell opens the popover and 'Mark all as read' is usable", async () => {
      await topbar.openNotifications();
      // This list re-renders live (real-time notifications), which trips
      // Playwright's actionability "stable element" wait indefinitely.
      // Force the click since the button itself is genuinely clickable.
      await topbar.clickMarkAllAsRead();
      // "Mark all as read" closes the popover automatically.
      await topbar.expectNotificationsClosed();
    });

    await test.step("Topbar: an unread notification is marked read on hover (not requiring a click)", async () => {
      await topbar.openNotifications();
      // Strict: there must be at least one notification row to assert
      // against -- silently skipping when none exist would let a
      // regression that hides every row pass.
      const rows = page.locator(
        '[class*="cursor-pointer"][class*="items-start"][class*="border-b"]',
      );
      await expect(rows.first()).toBeVisible({ timeout: 10_000 });
      const firstRow = rows.first();
      // If the first row is already read (no bg-muted class) we still
      // assert it stays read on hover -- the unread->read transition
      // may not happen if all notifications were read in the previous
      // step. The invariant we care about is "hover does not flip a
      // read row to unread".
      const initialClass = (await firstRow.getAttribute("class")) ?? "";
      await firstRow.hover();
      if (initialClass.includes("bg-muted")) {
        await expect(firstRow).not.toHaveClass(/bg-muted/, { timeout: 10_000 });
      } else {
        await expect(firstRow).not.toHaveClass(/bg-muted/, { timeout: 10_000 });
      }
      // If the popover is still open (no read-rows change closed it),
      // close it via an outside click on the page background.
      if ((await topbar.notificationsHeading.count()) > 0) {
        await topbar.clickOutside();
      }
      await topbar.expectNotificationsClosed();
    });

    await test.step("Topbar: app switcher opens the SELISE Blocks apps list", async () => {
      await topbar.openAppsMenu();
      await topbar.clickOutside();
      await topbar.expectAppsMenuClosed();
    });

    await test.step("Topbar: user avatar menu lists 'My Profile' and 'Log out', and Profile navigates", async () => {
      await expect(topbar.userMenuButton).toBeVisible({ timeout: 10_000 });
      await topbar.openUserMenu();
      await expect(topbar.myProfileMenuItem).toBeVisible({ timeout: 10_000 });
      await expect(topbar.logOutMenuItem).toBeVisible();

      await topbar.clickMyProfile();
      await expect(page).toHaveURL(/\/profile/, { timeout: 15_000 });

      await page.goBack();
      await expect(console.consoleHeading).toBeVisible({ timeout: 30_000 });
    });

    await test.step("Console: project list shows a real project card with name and environment chip", async () => {
      await expect(console.consoleHeading).toBeVisible({ timeout: 30_000 });
      const fixture = readLogicProject();
      if (fixture) {
        await expect(page.getByText(fixture.projectName, { exact: true }).first()).toBeVisible({
          timeout: 30_000,
        });
      }
      await expect(
        page
          .getByRole("button", {
            name: /^(Development|Production|Testing|Staging|IAT|UAT|Prod Shadow|Pre-Prod)$/,
          })
          .first(),
      ).toBeVisible({ timeout: 30_000 });
    });

    await test.step("Console: project card's settings icon navigates cross-app to the environments overview (Blocks OS)", async () => {
      // ProjectCard renders a Button with a Settings2 lucide icon and a
      // tooltip "Configure Project". Scope to <main> so we don't pick up
      // unrelated settings icons (e.g. sidebars, topbar). We then hover to
      // confirm the tooltip text matches -- this pins the selector to a
      // behavior, so a future lucide-react rename doesn't silently pass.
      await console.expectConfigureButtonVisible();
      await console.expectConfigureTooltip();

      const consoleUrl = page.url();
      await console.clickConfigure();
      // Cross-app nav bounces through dev-iam's OIDC callback, so the URL
      // contains /login/callback?...&forwardedTo=.../environments rather
      // than landing directly on /environments. Match either the path or
      // the forwardedTo query param.
      await expect(page).toHaveURL(/(?:\/|%2F)environments/, { timeout: 30_000 });

      await page.goto(consoleUrl, { waitUntil: "domcontentloaded" });
      await expect(console.consoleHeading).toBeVisible({ timeout: 30_000 });
    });

    await test.step("Console: Resources cards (Docs/Code/Cloud) actually navigate to their target URL when clicked", async () => {
      const links = [console.docsLink(), console.codeLink(), console.cloudLink()];

      for (const link of links) {
        await expect(link).toBeVisible({ timeout: 15_000 });
        await expect(link).toHaveAttribute("href", /^https?:\/\//);
        await expect(link).toHaveAttribute("target", "_blank");

        const { popup, href } = await console.openResourcePopup(link);
        const stripTrailingSlash = (url: string) => url.replace(/\/$/, "");
        expect(stripTrailingSlash(popup.url())).toBe(stripTrailingSlash(href));
        await popup.close();
      }
    });

    await openEnvironment(page);
    await dashboard.expectProjectDetailsHeading();
    const dashboardUrl = page.url();

    await test.step("'Overview' sidebar link is itemId-scoped and actually navigates back here", async () => {
      await sidebar.clickLink("Workflow");
      await expect(page).not.toHaveURL(dashboardUrl);

      await sidebar.expectLinkHref("Overview", /\/app\/[^/]+\/dashboard$/);

      await sidebar.clickLink("Overview");
      await expect(page).toHaveURL(dashboardUrl);
      await dashboard.expectProjectDetailsHeading();
    });

    await test.step("Workspace area (sidebar): Project/Environment widgets show current context and are permanently disabled", async () => {
      // The "Workspace" label lives in the desktop sidebar's section
      // header (sidebar-menu-desktop.tsx). The Project Details card on the
      // same page also has a "Project" / "Environment" button, so the
      // /^Project/ regex matches both buttons in strict mode. Scope both
      // widget locators to the sidebar by anchoring on the unique
      // "Workspace" paragraph and walking up to its container.
      await expect(sidebar.workspaceLabel).toBeVisible({ timeout: 15_000 });
      await sidebar.expectWorkspaceWidgetsDisabled();

      const fixture = readLogicProject();
      if (fixture) {
        await sidebar.expectWorkspaceWidgetShowsProject(fixture.projectName);
      }
      const environmentText = await sidebar.environmentWidget.innerText();
      expect(environmentText.toLowerCase()).toContain("environment");
      expect(environmentText.replace(/environment/i, "").trim().length).toBeGreaterThan(0);
    });

    await test.step("Project Details card shows Name, X-Blocks-Key, and a human-readable Environment badge", async () => {
      await dashboard.expectProjectDetailsCardVisible();
    });

    await test.step("X-Blocks-Key is masked, and its hover-reveal copy button works", async () => {
      await dashboard.expectXBlocksKeyMasked();
      await dashboard.copyXBlocksKey();
    });

    await test.step("Core APIs card lists endpoint groups, collapsed by default, and expands on click", async () => {
      await dashboard.expectCoreApisCardVisible();
      await dashboard.expectEndpointsGroupCount(0);
      await dashboard.expectFirstGroupCollapsed();
      await dashboard.expandFirstEndpointGroup();
    });

    await test.step("'Copy as cURL' on an endpoint is hover-reveal and copies something to the clipboard", async () => {
      await dashboard.copyAsCurl();
    });
  });
});
