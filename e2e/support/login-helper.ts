import { expect, type Page } from "@playwright/test"
import { e2eBaseUrl, e2eCredentials } from "./env"

function oidcEmailField(page: Page) {
  return page.locator("#oidc-email").or(page.getByRole("textbox", { name: "Work Email" }))
}

function oidcPasswordField(page: Page) {
  return page.locator("#oidc-password").or(page.getByRole("textbox", { name: "Password" }))
}

const consoleHeading = (page: Page) =>
  page.getByRole("heading", {
    name: /Your Blocks Projects|Welcome to SELISE Blocks/,
  })

/** True when the page is the product login gate or OIDC credential form. */
export async function isLoginSurface(page: Page): Promise<boolean> {
  if (
    await page
      .getByRole("button", { name: "Log in to your account" })
      .isVisible({ timeout: 500 })
      .catch(() => false)
  ) {
    return true
  }

  if (await oidcEmailField(page).isVisible({ timeout: 500 }).catch(() => false)) {
    return true
  }

  try {
    if (/\/login\/?$/i.test(new URL(page.url()).pathname)) return true
  } catch {
    // ignore invalid URL
  }

  return false
}


/** Claim this tab when another window holds the project session. */
export async function dismissSingleSessionTakeover(page: Page) {
  const leave = page
    .getByRole("button", { name: /^Leave / })
    .or(page.getByRole("button", { name: /Leave .+/ }))
  const heading = page.getByRole("heading", { name: /Your session is in / })
  // Dialog often paints after SPA hydrate; poll instead of a single short wait.
  for (let i = 0; i < 10; i++) {
    const leaveVisible = await leave.first().isVisible({ timeout: 800 }).catch(() => false)
    const headingVisible = await heading.isVisible({ timeout: 200 }).catch(() => false)
    if (leaveVisible || headingVisible) {
      await leave.first().click({ timeout: 5_000, force: true })
      await leave.first().waitFor({ state: "hidden", timeout: 15_000 }).catch(() => {})
      await page.waitForTimeout(500)
      return
    }
  }
}

async function safeGoto(page: Page, url: string) {
  for (let i = 0; i < 3; i++) {
    try {
      await page.goto(url, { waitUntil: "domcontentloaded", timeout: 60_000 })
      return
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error)
      if (!/ERR_ABORTED|interrupted|navigating/i.test(message) || i === 2) {
        throw error
      }
      await page.waitForTimeout(1_000)
    }
  }
}

async function fillCredentialsAndSubmit(page: Page) {
  const { email, password } = e2eCredentials()
  const emailField = oidcEmailField(page)
  await emailField.fill(email)
  const passwordField = oidcPasswordField(page)
  await expect(passwordField).toBeVisible({ timeout: 10_000 })
  await passwordField.fill(password)

  // IAM returns JSON { redirect_uri }; SPA navigates client-side. On PR previews
  // that hop can stall on AUTHENTICATING — follow the URI explicitly when needed.
  const loginRespPromise = page.waitForResponse(
    (r) => /\/api\/oidc\/login\/?$/.test(r.url()) && r.request().method() === "POST",
    { timeout: 90_000 },
  )
  await page.getByRole("button", { name: "Login", exact: true }).click()
  const loginResp = await loginRespPromise
  if (loginResp.ok()) {
    try {
      const body = (await loginResp.json()) as { redirect_uri?: string }
      if (body.redirect_uri) {
        // Give the SPA a moment to navigate itself; only force when still on IAM.
        await page.waitForTimeout(1_500)
        if (/dev-iam|\/oidc\/login/i.test(page.url())) {
          await safeGoto(page, body.redirect_uri)
        }
      }
    } catch {
      // non-JSON body — let waitForURL below handle navigation
    }
  }
}

export async function loginThroughOidc(page: Page, options?: { loginPath?: string }) {
  const base = e2eBaseUrl()
  const loginPath = options?.loginPath ?? `${base}/login`

  await safeGoto(page, loginPath)

  for (let attempt = 0; attempt < 3; attempt++) {
    if (await consoleHeading(page).isVisible({ timeout: 3_000 }).catch(() => false)) {
      return
    }

    const loginButton = page.getByRole("button", { name: "Log in to your account" })
    if (await loginButton.isVisible({ timeout: 3_000 }).catch(() => false)) {
      try {
        await loginButton.click({ timeout: 8_000 })
      } catch {
        if (await consoleHeading(page).isVisible({ timeout: 3_000 }).catch(() => false)) return
        await safeGoto(page, `${base}/app/console`)
        continue
      }

      const emailField = oidcEmailField(page)
      await Promise.race([
        emailField.waitFor({ state: "visible", timeout: 30_000 }),
        consoleHeading(page).waitFor({ state: "visible", timeout: 30_000 }),
        page.waitForURL(/\/app\/console/, { timeout: 30_000 }),
      ]).catch(() => {})

      if (await consoleHeading(page).isVisible().catch(() => false)) {
        return
      }

      if (await emailField.isVisible().catch(() => false)) {
        await fillCredentialsAndSubmit(page)
        await page.waitForURL(/\/app\/console/, { timeout: 90_000 })
        await expect(consoleHeading(page)).toBeVisible({ timeout: 30_000 })
        return
      }

      await safeGoto(page, `${base}/app/console`)
      continue
    }

    // Already on IAM credential form without the product login gate.
    if (await oidcEmailField(page).isVisible({ timeout: 2_000 }).catch(() => false)) {
      await fillCredentialsAndSubmit(page)
      await page.waitForURL(/\/app\/console/, { timeout: 90_000 })
      await expect(consoleHeading(page)).toBeVisible({ timeout: 30_000 })
      return
    }

    await safeGoto(page, `${base}/app/console`)
  }

  await safeGoto(page, `${base}/app/console`)
  await expect(consoleHeading(page)).toBeVisible({ timeout: 30_000 })
}

/**
 * Land on the product console; re-run OIDC when the saved session expired.
 * Idempotent when already authenticated.
 */
export async function ensureAuthenticated(page: Page) {
  const base = e2eBaseUrl()
  await safeGoto(page, `${base}/app/console`)
  await dismissSingleSessionTakeover(page)

  if (await consoleHeading(page).isVisible({ timeout: 15_000 }).catch(() => false)) {
    return
  }

  await loginThroughOidc(page)
  await dismissSingleSessionTakeover(page)
  await expect(consoleHeading(page)).toBeVisible({ timeout: 30_000 })
}

export async function ensureAuthenticatedOnCurrentOrigin(page: Page) {
  const href = page.url()
  if (!/^https?:/.test(href)) {
    await ensureAuthenticated(page)
    return
  }

  const origin = new URL(href).origin
  await safeGoto(page, `${origin}/app/console`)
  await dismissSingleSessionTakeover(page)

  if (await consoleHeading(page).isVisible({ timeout: 15_000 }).catch(() => false)) {
    return
  }

  await loginThroughOidc(page, { loginPath: `${origin}/login` })
  await dismissSingleSessionTakeover(page)
  await expect(consoleHeading(page)).toBeVisible({ timeout: 30_000 })
}

export async function loginFresh(page: Page) {
  await loginThroughOidc(page, { loginPath: e2eBaseUrl() })
}
