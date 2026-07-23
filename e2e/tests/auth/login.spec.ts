import { test, expect } from "../../support/test-base";
import { login } from "../../support/auth";

test.describe("Authentication", () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test("logs in and lands on the console", async ({ page }) => {
    await expect(page).toHaveURL(/\/app\/console/);
    await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible();
  });
});
