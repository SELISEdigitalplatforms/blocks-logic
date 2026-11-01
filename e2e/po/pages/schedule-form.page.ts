import { expect, type Locator, type Page } from "@playwright/test";
import { e2eBaseUrl } from "../../support/env";

/**
 * Schedule create/edit form — same `<ScheduleForm>` component renders both
 * modes (`mode="create" | "edit"`).
 *
 * `/app/:itemId/schedule/new`       — heading "Create Schedule", submit "Create"
 * `/app/:itemId/schedule/:id/edit`  — heading "Edit Schedule", submit "Save Changes"
 *
 * Field placeholders (the only stable locator across the Radix <Input>s):
 *   - Name         → "Schedule name"
 *   - Description      → "Brief summary of the scheduled task" (Textarea)
 *   - Cron Expression  → "0 0 * * *"
 *   - Webhook URL      → "https://api.example.com/webhook"
 *   - Signing Secret   → "Enter secret" (type=password)
 *   - Header Key       → "Header Key"
 *   - Header Value     → "Header Value"
 *
 * Validation messages (Zod resolver):
 *   - "Name is required"
 *   - "Cron expression is required"
 *   - "Webhook URL is required"
 *   - "Enter a valid URL"
 *   - "Cron expression must have 5 fields: minute hour day-of-month month day-of-week"
 *     (plus per-field range messages from cron-validation.ts)
 *   - "Payload must be valid JSON"
 *   - "End date must be after start date"
 */
export class ScheduleFormPage {
  constructor(private readonly page: Page) {}

  async gotoNew(itemId: string): Promise<void> {
    await this.page.goto(`${e2eBaseUrl()}/app/${itemId}/schedule/new`, {
      waitUntil: "domcontentloaded",
    });
  }

  async gotoEdit(itemId: string, scheduleId: string): Promise<void> {
    await this.page.goto(`${e2eBaseUrl()}/app/${itemId}/schedule/${scheduleId}/edit`, {
      waitUntil: "domcontentloaded",
    });
  }

  // ---- Heading + submit button --------------------------------------------

  get createScheduleHeading(): Locator {
    return this.page.getByRole("heading", { name: "Create Schedule" });
  }

  get editScheduleHeading(): Locator {
    return this.page.getByRole("heading", { name: "Edit Schedule" });
  }

  get createSubmit(): Locator {
    return this.page.getByRole("button", { name: "Create" });
  }

  get saveChangesSubmit(): Locator {
    return this.page.getByRole("button", { name: "Save Changes" });
  }

  get cancelButton(): Locator {
    return this.page.getByRole("button", { name: "Cancel" });
  }

  // ---- Field inputs (by stable placeholder) --------------------------------

  get nameInput(): Locator {
    return this.page.getByPlaceholder("Schedule name").first();
  }

  get descriptionInput(): Locator {
    return this.page.getByPlaceholder("Brief summary of the scheduled task");
  }

  get cronInput(): Locator {
    return this.page.getByPlaceholder("0 0 * * *").first();
  }

  get webhookUrlInput(): Locator {
    return this.page.getByPlaceholder("https://api.example.com/webhook").first();
  }

  get signingSecretInput(): Locator {
    return this.page.getByPlaceholder("Enter secret");
  }

  get headerKeyInput(): Locator {
    return this.page.getByPlaceholder("Header Key");
  }

  get headerValueInput(): Locator {
    return this.page.getByPlaceholder("Header Value");
  }

  // ---- Field fills (single-field) -----------------------------------------

  async fillName(value: string): Promise<void> {
    await this.nameInput.fill(value);
  }

  async fillCron(value: string): Promise<void> {
    await this.cronInput.fill(value);
  }

  async fillWebhookUrl(value: string): Promise<void> {
    await this.webhookUrlInput.fill(value);
  }

  async fillSigningSecret(value: string): Promise<void> {
    await this.signingSecretInput.fill(value);
  }

  async fillHeaderKey(value: string): Promise<void> {
    await this.headerKeyInput.fill(value);
  }

  async fillHeaderValue(value: string): Promise<void> {
    await this.headerValueInput.fill(value);
  }

  /**
   * Fill the three required fields with sensible test values.
   * Caller can override url/cron by passing the optional params.
   */
  async fillRequiredFields(
    name: string,
    url = "https://api.example.com/webhook",
    cron = "0 9 * * *",
  ): Promise<void> {
    await this.fillName(name);
    await this.fillWebhookUrl(url);
    await this.fillCron(cron);
  }

  // ---- Active switch + headers + cron presets ------------------------------

  get activeSwitch(): Locator {
    return this.page.getByRole("switch").first();
  }

  get addHeaderButton(): Locator {
    return this.page.getByRole("button", { name: "Add Header" });
  }

  async clickActiveSwitch(): Promise<void> {
    await this.activeSwitch.click();
  }

  async clickAddHeader(): Promise<void> {
    await this.addHeaderButton.click();
  }

  cronPresetButton(label: string): Locator {
    return this.page.getByRole("button", { name: label });
  }

  async selectCronPreset(label: string): Promise<void> {
    await this.cronPresetButton(label).click();
  }

  async expectCronValue(value: string): Promise<void> {
    await expect(this.cronInput).toHaveValue(value);
  }

  // ---- Submit + Cancel ----------------------------------------------------

  async clickSubmit(): Promise<void> {
    // Submit button is "Create" or "Save Changes" depending on mode.
    const submit = this.saveChangesSubmit.or(this.createSubmit);
    await submit.click();
  }

  async clickCancel(): Promise<void> {
    await this.cancelButton.click();
  }

  async expectStillOnCreate(): Promise<void> {
    await expect(this.page).toHaveURL(/\/schedule\/new$/);
  }

  async expectOnEdit(): Promise<void> {
    await expect(this.page).toHaveURL(/\/schedule\/[^/]+\/edit$/);
  }

  // ---- Validation message helpers ------------------------------------------

  async expectValidationError(text: string, timeout = 10_000): Promise<void> {
    await expect(this.page.getByText(text)).toBeVisible({ timeout });
  }

  // ---- Monaco editor (Payload field) ---------------------------------------

  /**
   * Write JSON into the Monaco editor that backs the Payload field.
   *
   * Monaco's `.inputarea` is hidden + readonly-resilient, so the cleanest
   * path is its own model API. We fall back to keyboard typing if the model
   * API is not yet mounted (Monaco is async).
   */
  async writeJsonPayload(json: string): Promise<void> {
    const wroteViaModel = await this.page.evaluate((value) => {
      type MonacoEditor = { getValue(): string; setValue(v: string): void };
      // Optional-chain both fields — Monaco exposes `monaco` early but
      // `monaco.editor` may be undefined until the editor instance is mounted.
      const w = window as unknown as {
        monaco?: { editor?: { getEditors(): unknown[] } };
      };
      const editors = w.monaco?.editor?.getEditors() as MonacoEditor[] | undefined;
      const editor = editors?.[0];
      if (!editor) return false;
      editor.setValue(value);
      return true;
    }, json);

    if (!wroteViaModel) {
      const editor = this.page.locator(".monaco-editor").first();
      await expect(editor).toBeVisible();
      await editor.click();
      await this.page.keyboard.press("Control+A");
      await this.page.keyboard.type(json);
    }
  }

  async expectCreateSubmitVisible(): Promise<void> {
    await expect(this.createSubmit).toBeVisible();
  }

  async expectSaveChangesSubmitVisible(): Promise<void> {
    await expect(this.saveChangesSubmit).toBeVisible();
  }

  // ---- Extra helpers (description, signing secret, remove header) ---------

  async fillDescription(value: string): Promise<void> {
    await this.descriptionInput.fill(value);
  }

  // ---- Timezone picker + next runs ------------------------------------------

  get timezoneTrigger(): Locator {
    return this.page.getByRole("combobox", { name: "Timezone" });
  }

  get timezoneHelper(): Locator {
    return this.page.getByText(/^Evaluated in .+\. Daylight-saving changes follow this timezone\.$/);
  }

  get nextRunItems(): Locator {
    return this.page.locator("[aria-live='polite'] li");
  }

  /** Opens the picker, searches, and picks the zone with exactly this id. */
  async selectTimezone(search: string, zoneId: string): Promise<void> {
    await this.timezoneTrigger.click();
    await this.page.getByPlaceholder("Search timezone…").fill(search);
    await this.page.getByRole("option", { name: new RegExp(`^${zoneId.replace("/", "\\/")}UTC`) }).click();
    await expect(this.timezoneTrigger).toContainText(zoneId);
  }

  /** Picks a day in the Start Date calendar, moving `monthsAhead` months forward first. */
  async pickStartDate(day: number, monthsAhead = 0): Promise<void> {
    await this.page.getByText("Start Date", { exact: true }).locator("xpath=following::button[1]").click();
    for (let i = 0; i < monthsAhead; i++) {
      await this.page.getByRole("button", { name: /next month/i }).click();
    }
    await this.page.getByRole("gridcell", { name: String(day), exact: true }).first().click();
  }

  /** Trash icon button next to a header row (removes the header from useFieldArray). */
  get removeHeaderButton(): Locator {
    return this.page.locator("button:has(svg.lucide-trash2)");
  }

  async clickRemoveHeader(index = 0): Promise<void> {
    await this.removeHeaderButton.nth(index).click();
  }
}
