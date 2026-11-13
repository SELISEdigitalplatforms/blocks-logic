import { expect, type Page } from "@playwright/test";
import { openSharedProjectDashboard } from "./suite-helpers";

export type CreatedFunction = { name: string; url: string };

const DETAIL_URL = /\/functions\/[^/?#]+(?:[?#].*)?$/;

/**
 * Load the shared project's dashboard as a new document (support/suite-helpers.ts, which recovers from a
 * console bounce), then move to `url` inside the app (history push + popstate, as a link click does).
 * A direct load of a deep project URL can land on the console; this way the page is always a fresh
 * document with the project selected, and nothing on the dashboard loads Monaco.
 */
export async function openInFreshDocument(page: Page, url: string): Promise<void> {
  await openSharedProjectDashboard(page);
  const target = new URL(url);
  await page.evaluate((path) => {
    window.history.pushState({}, "", path);
    window.dispatchEvent(new PopStateEvent("popstate"));
  }, target.pathname + target.search);
  await expect(page).toHaveURL(url, { timeout: 30_000 });
}

/**
 * Create a function through the Functions page: "New function" → the "Create function" dialog → Name → "Create"
 * (function-create-dialog.tsx, default starter; never tested or deployed). Lands on its detail page.
 */
export async function createFunctionInUi(page: Page, name: string): Promise<CreatedFunction> {
  await openSharedProjectDashboard(page);
  await page.getByRole("link", { name: "Functions" }).first().click();
  await page.getByRole("button", { name: "New function" }).first().click({ timeout: 60_000 });
  const dialog = page.getByRole("dialog", { name: "Create function" });
  await dialog.getByRole("textbox", { name: "Name" }).fill(name, { timeout: 30_000 });
  await dialog.getByRole("button", { name: "Create", exact: true }).click({ timeout: 30_000 });
  await expect(page).toHaveURL(DETAIL_URL, { timeout: 60_000 });
  return { name, url: page.url().replace(/[?#].*$/, "") };
}

/**
 * Delete it again: Configuration tab → Danger zone "Delete function" → type the name → "Delete function"
 * (delete-function-dialog.tsx).
 */
export async function deleteFunctionInUi(page: Page, fn: CreatedFunction): Promise<void> {
  await openInFreshDocument(page, fn.url);
  await page
    .getByRole("tab", { name: /^Configuration/ })
    .first()
    .click({ timeout: 60_000 });
  await page.getByRole("button", { name: "Delete function" }).first().click({ timeout: 30_000 });
  const dialog = page.getByRole("dialog", { name: "Delete function" });
  await dialog.getByRole("textbox").fill(fn.name, { timeout: 30_000 });
  await dialog
    .getByRole("button", { name: /^(Delete function|Delete anyway)$/ })
    .last()
    .click({ timeout: 30_000 });
  await expect(page.getByText("Function deleted successfully").first()).toBeVisible({
    timeout: 30_000,
  });
}
