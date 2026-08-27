import fs from "fs"
import path from "path"
import { type Page } from "@playwright/test"
import {
  openNamedProjectDashboard,
  waitForLogicDashboardReady,
} from "./create-and-delete-project"
import { e2eBaseUrl } from "./env"
import { ensureAuthenticated, isLoginSurface } from "./login-helper"
import { LOGIC_SESSION_PATH, readLogicProject } from "./logic-project"

async function persistSuiteSession(page: Page) {
  fs.mkdirSync(path.dirname(LOGIC_SESSION_PATH), { recursive: true })
  await page.context().storageState({ path: LOGIC_SESSION_PATH })
}

function sharedDashboardUrl(itemId: string): string {
  return `${e2eBaseUrl()}/app/${itemId}/dashboard`
}

async function reseedProjectContext(
  page: Page,
  projectName: string,
  dashboardUrl: string | undefined,
) {
  await openNamedProjectDashboard(page, projectName, { dashboardUrl })
  await persistSuiteSession(page)
}

/**
 * Open the shared suite project dashboard via direct URL.
 *
 * Happy path: `goto(/app/{itemId}/dashboard)` using session localStorage from
 * suite setup (must be saved AFTER the project was opened once).
 *
 * Recovery only (login expiry or console bounce): one env-chip open to reseed
 * localStorage, persist session, done — not used on every test.
 */
export async function openSharedProjectDashboard(page: Page) {
  const fixture = readLogicProject()
  if (!fixture?.itemId) {
    throw new Error(
      "Missing fixtures/logic-project.json (or itemId) — run the logic-setup project first " +
        "(suite.setup.spec.ts).",
    )
  }

  const targetUrl = sharedDashboardUrl(fixture.itemId)
  const fixtureDashboardUrl = fixture.dashboardUrl || targetUrl

  const gotoDashboard = async () => {
    await page.goto(targetUrl, { waitUntil: "domcontentloaded" })
  }

  await gotoDashboard()

  if (await isLoginSurface(page)) {
    await ensureAuthenticated(page)
    await reseedProjectContext(page, fixture.projectName, fixtureDashboardUrl)
    return
  }

  try {
    await waitForLogicDashboardReady(page, fixture.projectName)
    await persistSuiteSession(page)
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    const bouncedToConsole = /landed on the console/i.test(message)

    let pathname = ""
    try {
      pathname = new URL(page.url()).pathname
    } catch {
      pathname = ""
    }

    if (!bouncedToConsole && !/\/app\/console\/?$/i.test(pathname)) {
      throw error
    }

    await reseedProjectContext(page, fixture.projectName, fixtureDashboardUrl)
  }
}
