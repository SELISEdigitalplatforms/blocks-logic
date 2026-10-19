import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import type { IFunctionImportPreview, IFunctionImportResult } from "../../types/transfer.types";

const previewImport = vi.fn();
const applyImport = vi.fn();
const getBuild = vi.fn();
const deployFunction = vi.fn();
const navigate = vi.fn();

vi.mock("../../services/function.service", () => ({
  functionService: {
    previewImport: (...args: unknown[]) => previewImport(...args),
    applyImport: (...args: unknown[]) => applyImport(...args),
    getBuild: (...args: unknown[]) => getBuild(...args),
    deployFunction: (...args: unknown[]) => deployFunction(...args),
  },
}));
vi.mock("react-router", async (actual) => ({ ...(await actual<object>()), useNavigate: () => navigate }));
vi.mock("@seliseblocks/genesis-os", async (actual) => ({
  ...(await actual<object>()),
  useScopedPath: () => (path: string) => path,
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

import { FunctionImportDialog } from "./function-import-dialog";

const ID = "6f1c2a9e-1b2c-4d5e-8f90-123456789abc";
const FILE = {
  format: "blocks.function.export",
  schemaVersion: 1,
  exportedAt: "2026-10-18T00:00:00Z",
  function: { itemId: ID, name: "orders" },
  secretNames: [],
  contentHash: "abc",
};

const preview = (overrides: Partial<IFunctionImportPreview> = {}): IFunctionImportPreview => ({
  status: "new",
  functionId: ID,
  name: "orders",
  contentHash: "abc",
  targetName: null,
  targetLastUpdatedDate: null,
  blockedReason: null,
  fields: [],
  files: [],
  missingSecrets: [],
  duplicateSecrets: [],
  unresolvedSecretRefs: [],
  secretsUnchecked: false,
  allowInstallScriptsRequested: false,
  warnings: [],
  ...overrides,
});

const result = (overrides: Partial<IFunctionImportResult> = {}): IFunctionImportResult => ({
  status: "created",
  functionId: ID,
  name: "orders",
  allowInstallScriptsReset: false,
  missingSecrets: [],
  duplicateSecrets: [],
  messages: [],
  deploy: { state: "building", buildId: "b1" },
  ...overrides,
});

const upload = async (content: unknown = FILE) => {
  const user = userEvent.setup();
  renderWithProviders(<FunctionImportDialog open onOpenChange={vi.fn()} />);
  await user.upload(
    screen.getByTestId("function-import-input"),
    new File([typeof content === "string" ? content : JSON.stringify(content)], "f.json", { type: "application/json" }),
  );
  return user;
};

describe("FunctionImportDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("previews a new function, imports it, then deploys the build once it succeeds", async () => {
    previewImport.mockResolvedValue(preview());
    applyImport.mockResolvedValue(result());
    getBuild.mockResolvedValue({ id: "b1", status: "Succeeded", createdDate: "" });
    deployFunction.mockResolvedValue({ id: "v1", number: 1 });

    const user = await upload();

    expect((await screen.findByTestId("import-status")).textContent).toMatch(/not here yet/);
    expect(previewImport).toHaveBeenCalledWith(FILE);

    await user.click(screen.getByRole("button", { name: "Import" }));

    await waitFor(() => expect(applyImport).toHaveBeenCalled());
    expect(applyImport.mock.calls[0][0]).toEqual({
      file: FILE,
      resolution: undefined,
      fieldChoices: undefined,
      expectedLastUpdatedDate: undefined,
    });
    await waitFor(() => expect(deployFunction).toHaveBeenCalledWith({ functionId: ID, buildId: "b1" }));
    expect(await screen.findByText("Imported and live as v1.")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Open function" }));
    expect(navigate).toHaveBeenCalledWith(`functions/${ID}`);
  });

  it("shows the field table and a diff per file, and sends the per-item choices", async () => {
    previewImport.mockResolvedValue(
      preview({
        status: "changed",
        targetLastUpdatedDate: "2026-10-18T10:00:00.123Z",
        fields: [{ field: "trigger", label: "Trigger", mine: "{ a }", incoming: "{ b }" }],
        files: [
          { field: "indexJs", file: "index.js", language: "javascript", mine: "old()", incoming: "new()" },
          { field: "packageJson", file: "package.json", language: "json", mine: "{}", incoming: "{ }" },
        ],
      }),
    );
    applyImport.mockResolvedValue(result({ status: "updated" }));
    getBuild.mockResolvedValue({ id: "b1", status: "Building", createdDate: "" });

    const user = await upload();

    expect(await screen.findByText("Trigger")).toBeTruthy();
    expect(screen.getAllByTestId("mock-diff-editor").map((e) => e.textContent)).toEqual(["old()|new()", "{}|{ }"]);

    await user.click(screen.getAllByRole("button", { name: "Keep mine" })[1]); // index.js
    await user.click(screen.getByRole("button", { name: "Import" }));

    await waitFor(() => expect(applyImport).toHaveBeenCalled());
    expect(applyImport.mock.calls[0][0]).toEqual({
      file: FILE,
      resolution: "merge",
      fieldChoices: { trigger: "incoming", indexJs: "mine", packageJson: "incoming" },
      expectedLastUpdatedDate: "2026-10-18T10:00:00.123Z",
    });
    expect(await screen.findByText(/Building and deploying/)).toBeTruthy();
  });

  it("previews again when the function changed meanwhile (409)", async () => {
    previewImport.mockResolvedValue(preview({ status: "changed", fields: [{ field: "name", label: "Name", mine: "a", incoming: "b" }] }));
    applyImport.mockRejectedValue({ status: 409, errors: { isSuccess: false, errors: { conflict: "It changed." } } });

    const user = await upload();
    await screen.findByText("Name");
    await user.click(screen.getByRole("button", { name: "Import" }));

    await waitFor(() => expect(previewImport).toHaveBeenCalledTimes(2));
    expect(toasts.showErrorToast).toHaveBeenCalledWith({ errors: expect.stringContaining("It changed.") });
  });

  it("refuses a file that is not a function export before sending it", async () => {
    await upload({ format: "blocks.proxy.export" });

    expect(await screen.findByText("This is not a function export file.")).toBeTruthy();
    expect(previewImport).not.toHaveBeenCalled();
  });

  it("shows the server's refusal and the warnings", async () => {
    previewImport.mockRejectedValueOnce({ status: 400, errors: { isSuccess: false, errors: { invalid_request: "Bad hash." } } });
    await upload();
    expect(await screen.findByText("Bad hash.")).toBeTruthy();
  });

  it("lists warnings and offers nothing to import when it is the same", async () => {
    previewImport.mockResolvedValue(preview({ status: "same", warnings: ["No secret named stripe-key exists here."] }));

    await upload();

    expect(await screen.findByText("No secret named stripe-key exists here.")).toBeTruthy();
    expect((screen.getByRole("button", { name: "Import" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("keeps the import when the build fails and lets the user deploy again", async () => {
    previewImport.mockResolvedValue(preview());
    applyImport.mockResolvedValue(
      result({ deploy: { state: "failed", buildId: null, error: "the build failed: npm ERR" } }),
    );
    deployFunction.mockResolvedValue({ id: "v2", number: 2 });

    const user = await upload();
    await user.click(await screen.findByRole("button", { name: "Import" }));

    expect(await screen.findByText(/the deploy failed. The function is saved/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Deploy again" }));
    await waitFor(() => expect(deployFunction).toHaveBeenCalledWith({ functionId: ID }));
    expect(await screen.findByText("Imported and live as v2.")).toBeTruthy();
  });
});
