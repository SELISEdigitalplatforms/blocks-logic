import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { ProxyImportOutcome, ProxyImportResult } from "../types";
import { ProxyImportDialog } from "./proxy-import-dialog";

const previewImport = vi.fn();
const applyImport = vi.fn();

vi.mock("../services", () => ({
  proxyService: {
    previewImport: (...args: unknown[]) => previewImport(...args),
    applyImport: (...args: unknown[]) => applyImport(...args),
  },
}));

vi.mock("@monaco-editor/react", () => ({
  DiffEditor: ({ original, modified }: { original: string; modified: string }) => (
    <div data-testid="mock-diff-editor">
      {original}|{modified}
    </div>
  ),
}));

const toasts = vi.hoisted(() => ({ showErrorToast: vi.fn(), showSuccessToast: vi.fn() }));
vi.mock("@/hooks/use-toast", () => toasts);

const FILE = {
  format: "blocks.proxy.export",
  schemaVersion: 1,
  proxy: { itemId: "0123456789abcdef0123456789abcdef", slug: "stripe", name: "Stripe" },
  contentHash: "abc",
};

const result = (overrides: Partial<ProxyImportResult> = {}): ProxyImportResult => ({
  status: "new",
  reason: null,
  reasonCode: null,
  itemId: FILE.proxy.itemId,
  slug: "stripe",
  name: "Stripe",
  currentVersion: 1,
  historyExists: false,
  changes: [],
  missingSecrets: [],
  duplicateSecrets: [],
  secretCheckUnavailable: false,
  maskedValues: [],
  mineJson: null,
  incomingJson: null,
  applied: false,
  ...overrides,
});

const ok = (overrides: Partial<ProxyImportResult> = {}): ProxyImportOutcome => ({
  ok: true,
  result: result(overrides),
});

const jsonFile = (content: unknown, name = "proxy-stripe.json") =>
  new File([typeof content === "string" ? content : JSON.stringify(content)], name, {
    type: "application/json",
  });

const setup = async (file: File = jsonFile(FILE)) => {
  const onImported = vi.fn();
  const onOpenChange = vi.fn();
  // applyAccept off: the dialog itself must refuse a wrong type, not only the browser's picker.
  const user = userEvent.setup({ applyAccept: false });
  renderWithProviders(
    <ProxyImportDialog open onOpenChange={onOpenChange} onImported={onImported} />,
  );
  await user.upload(screen.getByLabelText("Proxy file"), file);
  return { user, onImported, onOpenChange };
};

describe("ProxyImportDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("previews a new proxy, warns about missing secrets and masked values, then imports it", async () => {
    previewImport.mockResolvedValue(
      ok({ missingSecrets: ["stripe-key"], maskedValues: ["header:Authorization"] }),
    );
    applyImport.mockResolvedValue(ok({ applied: true }));

    const { user, onImported } = await setup();

    expect((await screen.findByTestId("proxy-import-status")).textContent).toMatch(
      /Not in this environment/,
    );
    expect(previewImport).toHaveBeenCalledWith(FILE);
    expect(screen.getByTestId("proxy-import-status")).toBeTruthy();
    expect(screen.getByTestId("import-status-chip").textContent).toBe("New");
    expect(
      within(screen.getByTestId("import-secret-checklist")).getByText("stripe-key"),
    ).toBeTruthy();
    expect(screen.getByText("header:Authorization")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Import" }));

    // A success step with a way to open the new proxy, not an instant redirect.
    await user.click(await screen.findByRole("button", { name: "Open proxy" }));
    expect(onImported).toHaveBeenCalledWith(FILE.proxy.itemId);
    expect(applyImport).toHaveBeenCalledWith(
      expect.objectContaining({ file: FILE, resolution: undefined, expectedVersion: 1 }),
    );
    expect(toasts.showSuccessToast).toHaveBeenCalledWith({ description: "Proxy imported." });
  });

  it("shows field differences and sends only the chosen fields as a merge", async () => {
    previewImport.mockResolvedValue(
      ok({
        status: "changed",
        currentVersion: 7,
        changes: [
          { field: "name", label: "name", mine: "Old", incoming: "Stripe" },
          { field: "header:X-Env", label: "header X-Env", mine: "b", incoming: null },
        ],
        mineJson: '{"a":1}',
        incomingJson: '{"a":2}',
      }),
    );
    applyImport.mockResolvedValue(ok({ status: "changed", applied: true }));

    const { user } = await setup();

    expect(await screen.findByText(/2 of 2 changes will be taken/)).toBeTruthy();
    expect(screen.getByText(/version 7 here/)).toBeTruthy();
    const row = screen.getByRole("group", { name: "Use for header X-Env" });
    await user.click(within(row).getByRole("button", { name: "Mine" }));

    await user.click(screen.getByRole("button", { name: "Compare JSON" }));
    expect(screen.getByTestId("mock-diff-editor").textContent).toBe('{"a":1}|{"a":2}');

    await user.click(screen.getByRole("button", { name: "Apply 1 change" }));

    await waitFor(() =>
      expect(applyImport).toHaveBeenCalledWith({
        file: FILE,
        resolution: "merge",
        fieldChoices: { name: "incoming", "header:X-Env": "mine" },
        expectedVersion: 7,
      }),
    );
  });

  it("sends takeIncoming when every field is taken, and nothing when every field is kept", async () => {
    previewImport.mockResolvedValue(
      ok({
        status: "changed",
        currentVersion: 3,
        changes: [{ field: "name", label: "name", mine: "Old", incoming: "Stripe" }],
      }),
    );
    applyImport.mockResolvedValue(ok({ status: "changed", applied: true }));

    const { user } = await setup();
    await screen.findByText(/1 of 1 changes will be taken/);

    await user.click(screen.getByRole("button", { name: "Keep all mine" }));
    expect(
      (screen.getByRole("button", { name: "Nothing to apply" }) as HTMLButtonElement).disabled,
    ).toBe(true);

    await user.click(screen.getByRole("button", { name: "Take all incoming" }));
    await user.click(screen.getByRole("button", { name: "Apply 1 change" }));

    await waitFor(() =>
      expect(applyImport).toHaveBeenCalledWith(
        expect.objectContaining({
          resolution: "takeIncoming",
          fieldChoices: undefined,
          expectedVersion: 3,
        }),
      ),
    );
  });

  it("explains a blocked file and does not allow import", async () => {
    previewImport.mockResolvedValue(
      ok({ status: "blocked", reason: "Another proxy here already uses the address 'stripe'." }),
    );

    await setup();

    expect(await screen.findByText(/Another proxy here already uses the address/)).toBeTruthy();
    expect((screen.getByRole("button", { name: "Import" }) as HTMLButtonElement).disabled).toBe(
      true,
    );
  });

  it("shows why the server refused the file", async () => {
    previewImport.mockResolvedValue({
      ok: false,
      status: 400,
      code: "PROXY_VALIDATION",
      message: "The file was changed after it was exported. Export it again.",
      errors: ["The file was changed after it was exported. Export it again."],
      result: null,
    } satisfies ProxyImportOutcome);

    await setup();

    expect(await screen.findByText(/changed after it was exported/)).toBeTruthy();
    expect((screen.getByRole("button", { name: "Import" }) as HTMLButtonElement).disabled).toBe(
      true,
    );
  });

  it("re-previews when the proxy changed before apply (409 version conflict)", async () => {
    previewImport
      .mockResolvedValueOnce(
        ok({
          status: "changed",
          currentVersion: 7,
          changes: [{ field: "name", label: "name", mine: "A", incoming: "B" }],
        }),
      )
      .mockResolvedValueOnce(
        ok({
          status: "changed",
          currentVersion: 8,
          changes: [{ field: "name", label: "name", mine: "C", incoming: "B" }],
        }),
      );
    applyImport.mockResolvedValue({
      ok: false,
      status: 409,
      code: "PROXY_VERSION_CONFLICT",
      message: "Someone else changed this proxy.",
      errors: [],
      result: null,
    } satisfies ProxyImportOutcome);

    const { user, onImported } = await setup();
    await screen.findByText(/version 7 here/);
    await user.click(screen.getByRole("button", { name: "Apply 1 change" }));

    expect(await screen.findByText(/version 8 here/)).toBeTruthy();
    expect(previewImport).toHaveBeenCalledTimes(2);
    expect(toasts.showErrorToast).toHaveBeenCalled();
    expect(onImported).not.toHaveBeenCalled();
  });

  it("refuses a file that is not JSON or not a proxy export without calling the server", async () => {
    await setup(jsonFile("not json {"));
    expect(await screen.findByText("This file is not valid JSON.")).toBeTruthy();

    expect(previewImport).not.toHaveBeenCalled();
  });

  it("refuses a JSON file of another format", async () => {
    await setup(jsonFile({ format: "something.else" }));
    expect(await screen.findByText("This is not a Blocks proxy export file.")).toBeTruthy();
    expect(previewImport).not.toHaveBeenCalled();
  });

  it("refuses a function export, an old schema, a wrong type, an empty and an oversized file", async () => {
    const { user } = await setup(jsonFile({ format: "blocks.function.export" }));
    expect(await screen.findByText("This is a function export, not a proxy export.")).toBeTruthy();

    const input = screen.getByLabelText("Proxy file");
    await user.upload(input, jsonFile({ ...FILE, schemaVersion: 9 }));
    expect(await screen.findByText(/schema version 9/)).toBeTruthy();

    await user.upload(input, jsonFile(FILE, "proxy.TXT"));
    expect(await screen.findByText("Only .json export files can be imported.")).toBeTruthy();

    await user.upload(input, jsonFile("", "empty.json"));
    expect(await screen.findByText("This file is empty.")).toBeTruthy();

    const big = jsonFile("{}", "big.json");
    Object.defineProperty(big, "size", { value: 1024 * 1024 + 1 });
    await user.upload(input, big);
    expect(await screen.findByText(/larger than 1 MB/)).toBeTruthy();
    expect(previewImport).not.toHaveBeenCalled();
  });

  it("accepts a dropped file, highlights while dragging, and refuses more than one", async () => {
    previewImport.mockResolvedValue(ok());
    renderWithProviders(<ProxyImportDialog open onOpenChange={vi.fn()} />);
    const zone = screen.getByTestId("import-drop-zone");

    fireEvent.dragOver(zone, { dataTransfer: { files: [] } });
    expect(zone.getAttribute("data-dragging")).toBe("true");
    fireEvent.dragLeave(zone);
    expect(zone.getAttribute("data-dragging")).toBeNull();

    fireEvent.drop(zone, { dataTransfer: { files: [jsonFile(FILE), jsonFile(FILE, "b.json")] } });
    expect(await screen.findByText("Choose one file at a time.")).toBeTruthy();

    fireEvent.drop(zone, { dataTransfer: { files: [jsonFile(FILE)] } });
    expect(await screen.findByTestId("proxy-import-status")).toBeTruthy();
    expect(previewImport).toHaveBeenCalledWith(FILE);
  });

  it("opens the picker from the keyboard", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ProxyImportDialog open onOpenChange={vi.fn()} />);
    const click = vi.spyOn(screen.getByLabelText("Proxy file"), "click");

    screen.getByRole("button", { name: /Choose a proxy export file/ }).focus();
    await user.keyboard("{Enter}");
    await user.keyboard(" ");
    expect(click).toHaveBeenCalledTimes(2);
  });

  it("starts over when the file is removed, and on Escape", async () => {
    previewImport.mockResolvedValue(ok());
    const { user, onOpenChange } = await setup();
    await screen.findByTestId("proxy-import-status");

    await user.click(screen.getByRole("button", { name: "Remove proxy-stripe.json" }));
    expect(screen.getByTestId("import-drop-zone")).toBeTruthy();
    expect(screen.queryByTestId("proxy-import-status")).toBeNull();

    await user.keyboard("{Escape}");
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("lists duplicate secrets in the checklist and shows the blocked chip", async () => {
    previewImport.mockResolvedValue(
      ok({ status: "blocked", reason: "No.", duplicateSecrets: ["mail-key"] }),
    );
    await setup();
    const checklist = await screen.findByTestId("import-secret-checklist");
    expect(within(checklist).getByText("mail-key")).toBeTruthy();
    expect(screen.getByTestId("import-status-chip").textContent).toBe("Blocked");
  });

  it("says when the proxy is already the same", async () => {
    previewImport.mockResolvedValue(ok({ status: "same" }));
    await setup();
    expect((await screen.findByTestId("proxy-import-status")).textContent).toMatch(
      /already has exactly this proxy/,
    );
    expect((screen.getByRole("button", { name: "Import" }) as HTMLButtonElement).disabled).toBe(
      true,
    );
  });
});
