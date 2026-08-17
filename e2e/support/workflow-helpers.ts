import { expect, type Locator, type Page } from "@playwright/test";
import { loginFresh } from "./login-helper";

export async function rowHasWorkflow(row: Locator): Promise<boolean> {
  const isVisible = await row.isVisible().catch(() => false);
  if (!isVisible) return false;
  const isEmptyPlaceholder = await row
    .getByText("No results found.")
    .isVisible()
    .catch(() => false);
  if (isEmptyPlaceholder) return false;
  // A row can be visible but still mid-render (no cell text yet) while the
  // list is loading, before it either fills in or resolves to the empty
  // placeholder above — don't treat that transient blank state as a match.
  const text = await row.innerText().catch(() => "");
  return text.trim().length > 0;
}

// The list can be mid-transition (e.g. right after a delete's optimistic
// update, or before a refetch lands) when we check it, so poll for a stable
// answer instead of trusting a single snapshot.
export async function pollRowHasWorkflow(
  row: Locator,
  attempts = 5,
  intervalMs = 800,
): Promise<boolean> {
  let result = false;
  for (let i = 0; i < attempts; i++) {
    result = await rowHasWorkflow(row);
    if (result) return true;
    await row.page().waitForTimeout(intervalMs);
  }
  return result;
}

// The app pushes live updates over a websocket, which can keep re-rendering
// a target (e.g. table/list controls) fast enough that a plain click never
// sees a stable element within its actionability window. Retry, falling
// back to a forced click that skips the stability check.
// The canvas toolbar's "Open Node Library" button is icon-only — its label
// lives only inside a Radix tooltip, which never becomes part of the
// button's accessible name. So it can't be found via getByRole's `name`
// option; target it structurally instead (last button in the first
// toolbar group: Fit View, Zoom in, Zoom out, Organize, Open Node Library).
export function openNodeLibraryButton(page: Page): Locator {
  return page
    .locator("div.rounded-md.border.bg-background.p-1.shadow-md")
    .first()
    .getByRole("button")
    .last();
}

export async function resilientClick(locator: Locator, attempts = 3): Promise<void> {
  for (let i = 0; i < attempts; i++) {
    try {
      await locator.click({ timeout: 10000 });
      return;
    } catch (err) {
      if (i === attempts - 1) {
        await locator.click({ force: true });
        return;
      }
      console.log(err);
    }
  }
}

export async function loginAndOpenWorkflowList(page: Page) {
  await loginFresh(page);

  await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible({
    timeout: 50000,
  });
  // await createProject(page);
  await page
    .getByRole("button", { name: /Development/ })
    .first()
    .click();
  await expect(page).toHaveURL(/\/app\/[^/]+\/dashboard/, {
    timeout: 30000,
  });

  await page.getByRole("link", { name: "Workflow" }).click();
  await expect(page.getByRole("heading", { name: "Workflow" })).toBeVisible({
    timeout: 30000,
  });
}

// Some steps assume we're already inside a workflow's editor (from a prior
// step's navigation). If the app has bounced back to the Workflow list in
// between — e.g. after a stale/deleted test workflow, or a re-login that
// only restores the list — re-enter the first workflow's editor.
export async function ensureOnWorkflowEditor(page: Page) {
  const onEditor = await page
    .getByRole("tab", { name: "Editor" })
    .isVisible({ timeout: 3000 })
    .catch(() => false);
  if (onEditor) return;

  const firstRow = page.getByRole("row").nth(1);
  if (await pollRowHasWorkflow(firstRow)) {
    await resilientClick(firstRow.locator("td").first());
    await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15000 });
    await expect(page.getByRole("tab", { name: "Editor" })).toBeVisible();
  }
}

// Opens the first workflow row (creating one if the list is empty) and lands
// on its Editor tab. Shared entry point for specs that need to be inside a
// workflow's editor rather than on the list.
export async function openFirstWorkflow(page: Page) {
  await page.reload({ waitUntil: "domcontentloaded" });
  await page.waitForLoadState("networkidle").catch(() => {});
  await page
    .locator('[class*="skeleton"]')
    .first()
    .waitFor({ state: "hidden" })
    .catch(() => {});

  const emptyMessage = page.getByText("No results found.").first();
  const firstRow = page.getByRole("row").nth(1);

  await Promise.race([
    emptyMessage.waitFor({ state: "visible" }).catch(() => {}),
    firstRow.waitFor({ state: "visible" }).catch(() => {}),
  ]);

  if (!(await pollRowHasWorkflow(firstRow))) {
    await resilientClick(page.getByRole("button", { name: "Add Workflow" }));
    await page.getByLabel("Workflow Name").fill(`Order Processing ${Date.now()}`);
    await page.getByRole("button", { name: "Create" }).click();
    await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15000 });
  } else {
    await resilientClick(firstRow.locator("td").first());
    await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15000 });
  }
  await expect(page.getByRole("tab", { name: "Editor" })).toBeVisible();
}
