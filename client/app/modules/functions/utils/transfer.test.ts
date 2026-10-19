import { describe, expect, it } from "vitest";
import {
  MAX_IMPORT_BYTES,
  exportFileName,
  importErrorOf,
  readImportFile,
  secretNamesIn,
  soleSecretName,
} from "./transfer";
import type { IFunctionExportFile } from "../types/transfer.types";

const file = (content: string, name = "f.json") => new File([content], name, { type: "application/json" });

describe("transfer utils", () => {
  it("reads a function export", async () => {
    const read = await readImportFile(file(JSON.stringify({ format: "blocks.function.export", schemaVersion: 1 })));
    expect(read.format).toBe("blocks.function.export");
  });

  it.each([
    ["not json", "{", "not valid JSON"],
    ["another format", JSON.stringify({ format: "blocks.proxy.export" }), "not a function export"],
    ["an array", "[]", "not a function export"],
  ])("refuses %s before sending anything", async (_, content, message) => {
    await expect(readImportFile(file(content))).rejects.toThrow(message);
  });

  it("refuses a file over the server's 3 MB ceiling", async () => {
    await expect(readImportFile(file("x".repeat(MAX_IMPORT_BYTES + 1)))).rejects.toThrow("3 MB");
  });

  it("names the download like the server does", () => {
    const name = exportFileName({
      function: { name: "Send Order: Mail!", itemId: "6f1c2a9e-1b2c-4d5e-8f90-123456789abc" },
    } as IFunctionExportFile);
    expect(name).toBe("function-send-order-mail-6f1c2a9e.json");
  });

  it("reads the status and message of a failed call", () => {
    expect(
      importErrorOf({ status: 409, errors: { isSuccess: false, errors: { conflict: "It changed." } } }),
    ).toEqual({ status: 409, message: "It changed." });
    expect(importErrorOf(new Error("boom")).message).toBe("boom");
    expect(importErrorOf(null).message).toBe("The request could not be completed.");
  });

  it("finds secret names", () => {
    expect(soleSecretName(" {{secretName.stripe-key}} ")).toBe("stripe-key");
    expect(soleSecretName("Bearer {{secretName.stripe-key}}")).toBeNull();
    expect(soleSecretName("{{secret.abc}}")).toBeNull();
    expect(secretNamesIn("{{secretName.a}}:{{secretName.b.c}}")).toEqual(["a", "b.c"]);
  });
});
