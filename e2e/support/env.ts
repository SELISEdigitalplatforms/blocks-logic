export function requireEnv(name: string): string {
  const value = process.env[name]
  if (!value) {
    throw new Error(`${name} is not set. Fill it in e2e/.env.e2e.`)
  }
  return value
}

export function e2eBaseUrl(): string {
  return requireEnv("E2E_BASE_URL")
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
