import { type Page } from "@playwright/test";

export async function loginFresh(page: Page) {
  const email = process.env.E2E_USERNAME;
  const password = process.env.E2E_PASSWORD;
  if (!email || !password) {
    throw new Error("E2E_USERNAME/E2E_PASSWORD are not set. Set them in e2e/.env.e2e.");
  }

  await page.goto(process.env.E2E_BASE_URL ?? "");
  await page.getByRole("button", { name: "Log in to your account" }).click();
  await page.getByRole("textbox", { name: "Work Email" }).click();
  await page.getByRole("textbox", { name: "Work Email" }).fill(email);
  await page.getByRole("textbox", { name: "Password" }).click();
  await page.getByRole("textbox", { name: "Password" }).fill(password);
  await page.getByRole("button", { name: "Login" }).click();
  await page.waitForURL(/\/app\/console/, { timeout: 45_000 });
}
