function stripTrailingSlash(url: string): string {
  return url.replace(/\/$/, "")
}

export function requireEnv(name: string): string {
  const value = process.env[name]
  if (!value) {
    throw new Error(`${name} is not set. Fill it in e2e/.env.e2e.`)
  }
  return value
}

/** Blocks Logic app under test (`E2E_BASE_URL`). */
export function e2eBaseUrl(): string {
  return stripTrailingSlash(requireEnv("E2E_BASE_URL"))
}

export function e2eProjectId(): string | undefined {
  const value = process.env.E2E_PROJECT_ID?.trim()
  return value || undefined
}

/**
 * Derive Blocks OS origin from the Logic base URL.
 *
 * | Logic (`E2E_BASE_URL`)                          | OS (derived)                               |
 * |------------------------------------------------|--------------------------------------------|
 * | https://dev-logic.blocksdevelopers.com[:port]  | https://dev-os.blocksdevelopers.com[:port] |
 * | https://logic.seliseblocks.com                 | https://os.seliseblocks.com                |
 *
 * Override anytime with `E2E_OS_BASE_URL`.
 */
export function deriveOsBaseUrlFromLogic(logicBaseUrl: string): string | undefined {
  let url: URL
  try {
    url = new URL(logicBaseUrl)
  } catch {
    return undefined
  }

  if (/^dev-logic\./i.test(url.hostname)) {
    url.hostname = url.hostname.replace(/^dev-logic\./i, "dev-os.")
    return stripTrailingSlash(url.origin)
  }

  if (/^logic\./i.test(url.hostname)) {
    url.hostname = url.hostname.replace(/^logic\./i, "os.")
    return stripTrailingSlash(url.origin)
  }

  return undefined
}

/** Blocks OS — create-project wizard + project delete (Logic has no Delete UI). */
export function e2eOsBaseUrl(): string {
  const explicit = process.env.E2E_OS_BASE_URL?.trim()
  if (explicit) return stripTrailingSlash(explicit)

  const derived = deriveOsBaseUrlFromLogic(e2eBaseUrl())
  if (derived) return derived

  throw new Error(
    "E2E_OS_BASE_URL is not set and could not be derived from E2E_BASE_URL. " +
      "Examples:\n" +
      "  Dev:  E2E_BASE_URL=https://dev-logic.blocksdevelopers.com  → OS https://dev-os.blocksdevelopers.com\n" +
      "  Prod: E2E_BASE_URL=https://logic.seliseblocks.com          → OS https://os.seliseblocks.com\n" +
      "Or set E2E_OS_BASE_URL explicitly in e2e/.env.e2e.",
  )
}

export function e2eCredentials(): { email: string; password: string } {
  return {
    email: requireEnv("E2E_USERNAME"),
    password: requireEnv("E2E_PASSWORD"),
  }
}

export function e2eTestEmailDomain(): string {
  return process.env.E2E_TEST_EMAIL_DOMAIN ?? "example.com"
}

export function uniqueTestEmail(localPart = "e2e"): string {
  return `${localPart}.${Date.now()}@${e2eTestEmailDomain()}`
}
