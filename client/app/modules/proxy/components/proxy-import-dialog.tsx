import { type ReactNode, useRef, useState } from "react";
import { DiffEditor } from "@monaco-editor/react";
import { useTheme } from "@seliseblocks/genesis-os/hooks";
import { AlertTriangle, FileUp, Info, Loader2 } from "lucide-react";
import { Alert, AlertDescription } from "@/components/ui-kits/alert/alert";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { cn } from "@/lib/utils";
import { useApplyProxyImport, usePreviewProxyImport } from "../hooks";
import { ProxyExportFile, ProxyImportOutcome, ProxyImportResult } from "../types";
import { readImportFile } from "../utils";

type Choice = "mine" | "incoming";

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Called after a write, with the imported proxy's id. */
  onImported?: (proxyId: string) => void;
};

const VERSION_CONFLICT = "PROXY_VERSION_CONFLICT";

const STATUS_TEXT: Record<ProxyImportResult["status"], string> = {
  new: "Not in this environment yet. Importing creates it with the same id and address.",
  same: "This environment already has exactly this proxy. There is nothing to import.",
  changed: "This proxy is already here and some fields differ. Choose what to keep.",
  blocked: "This file cannot be imported here.",
};

/** A history-style value: `null` means the field is not set on that side. */
const Value = ({ value }: { value: string | null }) =>
  value === null ? (
    <span className="italic text-muted-foreground">not set</span>
  ) : (
    <span className="break-all font-mono">{value}</span>
  );

/**
 * Import one proxy from an export file. The preview writes nothing; Apply sends the same file back and
 * the server checks everything again, so what the dialog shows is advice, never the authority.
 */
export const ProxyImportDialog = ({ open, onOpenChange, onImported }: Props) => {
  const inputRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<ProxyExportFile | null>(null);
  const [fileName, setFileName] = useState("");
  const [readError, setReadError] = useState<string | null>(null);
  const [outcome, setOutcome] = useState<ProxyImportOutcome | null>(null);
  const [choices, setChoices] = useState<Record<string, Choice>>({});
  const [applyError, setApplyError] = useState<string | null>(null);
  const [showJson, setShowJson] = useState(false);
  const { resolvedTheme } = useTheme();

  const preview = usePreviewProxyImport();
  const apply = useApplyProxyImport();

  const close = () => {
    setFile(null);
    setFileName("");
    setReadError(null);
    setOutcome(null);
    setChoices({});
    setApplyError(null);
    setShowJson(false);
    onOpenChange(false);
  };

  /** Every changed field starts as "take the file's value": the user opened Import to import. */
  const runPreview = async (next: ProxyExportFile) => {
    setApplyError(null);
    const result = await preview.mutateAsync(next);
    setOutcome(result);
    setChoices(
      result.ok
        ? Object.fromEntries(result.result.changes.map((c) => [c.field, "incoming" as Choice]))
        : {},
    );
  };

  const pick = async (picked: File | undefined) => {
    setOutcome(null);
    setReadError(null);
    setApplyError(null);
    if (!picked) return;
    setFileName(picked.name);
    const read = await readImportFile(picked);
    if (!read.ok) {
      setFile(null);
      setReadError(read.error);
      return;
    }
    setFile(read.file);
    await runPreview(read.file);
  };

  const result = outcome?.ok ? outcome.result : (outcome?.result ?? null);
  const changes = outcome?.ok ? outcome.result.changes : [];
  const takeCount = changes.filter((c) => choices[c.field] === "incoming").length;
  const canApply =
    !!file &&
    outcome?.ok === true &&
    (outcome.result.status === "new" || (outcome.result.status === "changed" && takeCount > 0));

  const setAll = (choice: Choice) =>
    setChoices(Object.fromEntries(changes.map((c) => [c.field, choice])));

  const confirm = async () => {
    if (!file || !outcome?.ok) return;
    setApplyError(null);
    const status = outcome.result.status;
    const allIncoming = takeCount === changes.length;
    const response = await apply.mutateAsync({
      file,
      resolution: status === "changed" ? (allIncoming ? "takeIncoming" : "merge") : undefined,
      fieldChoices: status === "changed" && !allIncoming ? choices : undefined,
      expectedVersion: outcome.result.currentVersion,
    });

    if (response.ok) {
      const done = response.result;
      showSuccessToast({
        description: !done.applied
          ? "Nothing to change. The proxy already matches."
          : status === "new"
            ? "Proxy imported."
            : "Proxy updated from the file.",
      });
      if (done.applied) onImported?.(done.itemId);
      close();
      return;
    }

    if (response.code === VERSION_CONFLICT) {
      // Someone saved meanwhile: show the fresh difference instead of applying old choices to it.
      showErrorToast({
        errors: "This proxy changed while you were looking. Check the new preview and apply again.",
      });
      await runPreview(file);
      return;
    }

    if (response.result) {
      setOutcome(response);
      return;
    }

    setApplyError(response.message);
  };

  return (
    <Dialog open={open} onOpenChange={(next) => (next ? onOpenChange(true) : close())}>
      <DialogContent className="max-h-[90vh] max-w-4xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Import proxy</DialogTitle>
          <DialogDescription>
            Choose a proxy file exported from another environment. You see what will change before
            anything is saved.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-wrap items-center gap-3">
          <input
            ref={inputRef}
            type="file"
            accept=".json,application/json"
            className="hidden"
            aria-label="Proxy file"
            onChange={(event) => {
              void pick(event.target.files?.[0]);
              event.target.value = "";
            }}
          />
          <Button
            type="button"
            size="sm"
            variant="outline"
            className="gap-1.5"
            disabled={preview.isPending || apply.isPending}
            onClick={() => inputRef.current?.click()}
          >
            {preview.isPending ? (
              <Loader2 className="h-3.5 w-3.5 animate-spin" />
            ) : (
              <FileUp className="h-3.5 w-3.5" />
            )}
            {fileName ? "Choose another file" : "Choose file"}
          </Button>
          {fileName ? (
            <span className="truncate text-xs text-muted-foreground">{fileName}</span>
          ) : null}
        </div>

        {readError ? (
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertDescription className="text-xs">{readError}</AlertDescription>
          </Alert>
        ) : null}

        {outcome && !outcome.ok && !outcome.result ? (
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertDescription className="text-xs">
              <p>{outcome.message}</p>
              {outcome.errors.filter((e) => e !== outcome.message).length ? (
                <ul className="mt-1 list-disc space-y-0.5 pl-4">
                  {outcome.errors
                    .filter((e) => e !== outcome.message)
                    .map((message) => (
                      <li key={message}>{message}</li>
                    ))}
                </ul>
              ) : null}
            </AlertDescription>
          </Alert>
        ) : null}

        {result ? (
          <div className="space-y-3" data-testid="proxy-import-preview">
            <div>
              <p className="text-sm font-semibold">{result.name}</p>
              <p className="font-mono text-xs text-muted-foreground">{result.slug}</p>
              <p className="mt-2 text-sm" data-testid="proxy-import-status">
                {STATUS_TEXT[result.status]}
              </p>
            </div>

            {result.status === "blocked" ? (
              <Alert variant="destructive">
                <AlertTriangle className="h-4 w-4" />
                <AlertDescription className="text-xs">
                  {result.reason ?? "This file cannot be imported here."}
                </AlertDescription>
              </Alert>
            ) : null}

            {result.status === "new" && result.historyExists ? (
              <Alert>
                <Info className="h-4 w-4" />
                <AlertDescription className="text-xs">
                  A proxy with this id was deleted here before. Its change history is kept, and the
                  import continues it from version {result.currentVersion}.
                </AlertDescription>
              </Alert>
            ) : null}

            {result.status === "changed" && changes.length ? (
              <div>
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <p className="text-xs font-medium">
                    {changes.length} {changes.length === 1 ? "field differs" : "fields differ"}
                    {result.currentVersion ? ` from version ${result.currentVersion} here` : ""}
                  </p>
                  <div className="flex gap-1">
                    <Button
                      type="button"
                      size="xs"
                      variant="ghost"
                      onClick={() => setAll("incoming")}
                    >
                      Take all from file
                    </Button>
                    <Button type="button" size="xs" variant="ghost" onClick={() => setAll("mine")}>
                      Keep all mine
                    </Button>
                    {result.mineJson && result.incomingJson ? (
                      <Button
                        type="button"
                        size="xs"
                        variant="ghost"
                        onClick={() => setShowJson((value) => !value)}
                      >
                        {showJson ? "Hide JSON" : "Compare JSON"}
                      </Button>
                    ) : null}
                  </div>
                </div>
                <Table className="mt-2 text-xs">
                  <TableHeader>
                    <TableRow>
                      <TableHead>Field</TableHead>
                      <TableHead>This environment</TableHead>
                      <TableHead>File</TableHead>
                      <TableHead className="w-[150px]">Use</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {changes.map((change) => (
                      <TableRow key={change.field}>
                        <TableCell className="font-medium">{change.label}</TableCell>
                        <TableCell>
                          <Value value={change.mine} />
                        </TableCell>
                        <TableCell>
                          <Value value={change.incoming} />
                        </TableCell>
                        <TableCell>
                          <div
                            className="flex gap-1"
                            role="group"
                            aria-label={`Use for ${change.label}`}
                          >
                            {(["mine", "incoming"] as const).map((choice) => (
                              <Button
                                key={choice}
                                type="button"
                                size="xs"
                                variant={choices[change.field] === choice ? "default" : "outline"}
                                aria-pressed={choices[change.field] === choice}
                                onClick={() =>
                                  setChoices((current) => ({ ...current, [change.field]: choice }))
                                }
                              >
                                {choice === "mine" ? "Keep" : "Take"}
                              </Button>
                            ))}
                          </div>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
                {showJson && result.mineJson && result.incomingJson ? (
                  <div className="mt-3 overflow-hidden rounded-lg border">
                    <DiffEditor
                      height="320px"
                      language="json"
                      theme={resolvedTheme === "dark" ? "vs-dark" : "vs"}
                      original={result.mineJson}
                      modified={result.incomingJson}
                      options={{
                        readOnly: true,
                        renderSideBySide: true,
                        minimap: { enabled: false },
                        scrollBeyondLastLine: false,
                        automaticLayout: true,
                        fontSize: 12,
                      }}
                    />
                    <p className="border-t px-3 py-1.5 text-[11px] text-muted-foreground">
                      Left: this environment. Right: the file. Hidden values show as ***.
                    </p>
                  </div>
                ) : null}
              </div>
            ) : null}

            {result.missingSecrets.length ? (
              <WarningNote>
                These secrets are not in this environment:{" "}
                <span className="font-mono">{result.missingSecrets.join(", ")}</span>. The proxy
                still imports, but its calls fail until you add a secret with the same name in
                Blocks OS Secrets. Nothing else needs to change.
              </WarningNote>
            ) : null}

            {result.duplicateSecrets.length ? (
              <WarningNote>
                More than one secret here has the name{" "}
                <span className="font-mono">{result.duplicateSecrets.join(", ")}</span>. Keep one
                secret per name so the right one is used.
              </WarningNote>
            ) : null}

            {result.secretCheckUnavailable ? (
              <WarningNote>
                Secrets could not be checked right now. You can still import.
              </WarningNote>
            ) : null}

            {result.maskedValues.length && result.status !== "blocked" ? (
              <Alert>
                <Info className="h-4 w-4" />
                <AlertDescription className="text-xs">
                  The file hides{" "}
                  {result.maskedValues.length === 1
                    ? "one value"
                    : `${result.maskedValues.length} values`}{" "}
                  as <span className="font-mono">***</span> (
                  <span className="font-mono">{result.maskedValues.join(", ")}</span>). A value this
                  environment already has is kept. Otherwise <span className="font-mono">***</span>{" "}
                  is saved — set the real value after the import, ideally as a{" "}
                  <span className="font-mono">{"{{$VAR.name}}"}</span> secret.
                </AlertDescription>
              </Alert>
            ) : null}
          </div>
        ) : null}

        {applyError ? (
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertDescription className="text-xs">{applyError}</AlertDescription>
          </Alert>
        ) : null}

        <DialogFooter>
          <Button type="button" variant="outline" onClick={close}>
            {result?.status === "same" ? "Close" : "Cancel"}
          </Button>
          <Button type="button" disabled={!canApply || apply.isPending} onClick={confirm}>
            {apply.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            {result?.status === "changed"
              ? takeCount
                ? `Apply ${takeCount} ${takeCount === 1 ? "change" : "changes"}`
                : "Nothing to apply"
              : "Import"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};

const WarningNote = ({ children }: { children: ReactNode }) => (
  <p
    className={cn(
      "flex items-start gap-1.5 rounded-md border border-amber-300/60 bg-amber-50 p-2 text-xs",
      "text-amber-800 dark:border-amber-500/40 dark:bg-amber-500/10 dark:text-amber-300",
    )}
  >
    <AlertTriangle aria-hidden="true" className="mt-0.5 h-3.5 w-3.5 shrink-0" />
    <span>{children}</span>
  </p>
);
