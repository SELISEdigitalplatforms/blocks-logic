import { useRef, useState } from "react";
import { DiffEditor } from "@monaco-editor/react";
import { useTheme } from "@seliseblocks/genesis-os/hooks";
import { AlertTriangle, CheckCircle2, Info, Loader2 } from "lucide-react";
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
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useApplyProxyImport, usePreviewProxyImport } from "../hooks";
import { ProxyExportFile, ProxyImportOutcome, ProxyImportResult } from "../types";
import { checkImportFile, readImportFile } from "../utils";
import {
  ImportChangeRow,
  ImportDropZone,
  ImportFileCard,
  ImportSecretChecklist,
  ImportStatusChip,
  ImportSteps,
  ImportValuePair,
} from "./proxy-import-parts";

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

type Done = { itemId: string; applied: boolean; status: ProxyImportResult["status"] };

/**
 * Import one proxy from an export file. The preview writes nothing; Apply sends the same file back and
 * the server checks everything again, so what the dialog shows is advice, never the authority.
 */
export const ProxyImportDialog = ({ open, onOpenChange, onImported }: Props) => {
  const inputRef = useRef<HTMLInputElement>(null);
  // Bumped on every pick and every reset: an answer that arrives for an older file is dropped.
  const ticket = useRef(0);
  const [file, setFile] = useState<ProxyExportFile | null>(null);
  const [picked, setPicked] = useState<{ name: string; size: number } | null>(null);
  const [checking, setChecking] = useState(false);
  const [readError, setReadError] = useState<string | null>(null);
  const [outcome, setOutcome] = useState<ProxyImportOutcome | null>(null);
  const [choices, setChoices] = useState<Record<string, Choice>>({});
  const [applyError, setApplyError] = useState<string | null>(null);
  const [done, setDone] = useState<Done | null>(null);
  const [showJson, setShowJson] = useState(false);
  const { resolvedTheme } = useTheme();

  const preview = usePreviewProxyImport();
  const apply = useApplyProxyImport();

  const reset = () => {
    ticket.current += 1;
    setFile(null);
    setPicked(null);
    setChecking(false);
    setReadError(null);
    setOutcome(null);
    setChoices({});
    setApplyError(null);
    setDone(null);
    setShowJson(false);
  };

  const close = () => {
    reset();
    onOpenChange(false);
  };

  /** Every changed field starts as "take the file's value": the user opened Import to import. */
  const runPreview = async (next: ProxyExportFile, mine = ticket.current) => {
    setApplyError(null);
    const answer = await preview.mutateAsync(next);
    if (mine !== ticket.current) return;
    setOutcome(answer);
    setChoices(
      answer.ok
        ? Object.fromEntries(answer.result.changes.map((c) => [c.field, "incoming" as Choice]))
        : {},
    );
  };

  /** One file in, from the picker or a drop: refuse what is clearly wrong, then preview. */
  const pick = async (files: File[]) => {
    reset();
    const mine = ticket.current;
    const chosen = files[0];
    if (!chosen) return;
    if (files.length > 1) {
      setReadError("Choose one file at a time.");
      return;
    }
    // The cheap checks run before the file is read, so a wrong file never costs a read.
    const refusal = checkImportFile(chosen);
    if (refusal) {
      setReadError(refusal);
      return;
    }
    setPicked({ name: chosen.name, size: chosen.size });
    setChecking(true);
    const read = await readImportFile(chosen);
    if (mine !== ticket.current) return;
    setChecking(false);
    if (!read.ok) {
      setPicked(null);
      setReadError(read.error);
      return;
    }
    setFile(read.file);
    await runPreview(read.file, mine);
  };

  const result = outcome?.ok ? outcome.result : (outcome?.result ?? null);
  const changes = outcome?.ok ? outcome.result.changes : [];
  const takeCount = changes.filter((c) => choices[c.field] === "incoming").length;
  const canApply =
    !!file &&
    !done &&
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
      const written = response.result;
      showSuccessToast({
        description: !written.applied
          ? "Nothing to change. The proxy already matches."
          : status === "new"
            ? "Proxy imported."
            : "Proxy updated from the file.",
      });
      setDone({ itemId: written.itemId, applied: !!written.applied, status });
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

  const busy = checking || preview.isPending || apply.isPending;
  const step = done ? 2 : result ? 1 : 0;

  return (
    <Dialog open={open} onOpenChange={(next) => (next ? onOpenChange(true) : close())}>
      <DialogContent className="max-h-[90vh] w-[min(96vw,1100px)] max-w-none overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Import proxy</DialogTitle>
          <DialogDescription>
            Choose a proxy export file (.json) from another environment. You see what will change
            before anything is saved.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <ImportSteps current={step} />

          <input
            ref={inputRef}
            type="file"
            accept=".json,application/json"
            className="hidden"
            aria-label="Proxy file"
            onChange={(event) => {
              void pick([...(event.target.files ?? [])]);
              event.target.value = "";
            }}
          />

          {!picked && !done ? (
            <ImportDropZone
              noun="proxy"
              disabled={busy}
              onBrowse={() => inputRef.current?.click()}
              onDropFiles={(files) => void pick(files)}
            />
          ) : null}

          {picked && !done ? (
            <ImportFileCard
              name={picked.name}
              size={picked.size}
              checking={checking || preview.isPending}
              disabled={apply.isPending}
              onReplace={() => inputRef.current?.click()}
              onRemove={reset}
            />
          ) : null}

          {readError ? (
            <Alert variant="destructive" role="alert">
              <AlertTriangle className="h-4 w-4" />
              <AlertDescription className="text-xs">{readError}</AlertDescription>
            </Alert>
          ) : null}

          {outcome && !outcome.ok && !outcome.result ? (
            <Alert variant="destructive" role="alert">
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

          {result && !done ? (
            <div className="flex flex-col gap-4" data-testid="proxy-import-preview">
              <div className="flex flex-col gap-1.5">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-sm font-semibold">{result.name}</span>
                  <ImportStatusChip status={result.status} />
                  <span className="font-mono text-xs font-normal text-medium-emphasis">
                    {result.slug}
                  </span>
                </div>
                <p className="text-sm" data-testid="proxy-import-status">
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
                    A proxy with this id was deleted here before. Its change history is kept, and
                    the import continues it from version {result.currentVersion}.
                  </AlertDescription>
                </Alert>
              ) : null}

              <ImportSecretChecklist
                missing={result.missingSecrets}
                duplicate={result.duplicateSecrets}
                unchecked={result.secretCheckUnavailable}
              />

              {result.maskedValues.length && result.status !== "blocked" ? (
                <Alert>
                  <Info className="h-4 w-4" />
                  <AlertDescription className="text-xs">
                    The file hides{" "}
                    {result.maskedValues.length === 1
                      ? "one value"
                      : `${result.maskedValues.length} values`}{" "}
                    as <span className="font-mono">***</span> (
                    <span className="font-mono">{result.maskedValues.join(", ")}</span>). A value
                    this environment already has is kept. Otherwise{" "}
                    <span className="font-mono">***</span> is saved — set the real value after the
                    import, ideally as a <span className="font-mono">{"{{$VAR.name}}"}</span>{" "}
                    secret.
                  </AlertDescription>
                </Alert>
              ) : null}

              {result.status === "changed" && changes.length ? (
                <div className="flex flex-col gap-3">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-sm text-medium-emphasis" aria-live="polite">
                      {takeCount} of {changes.length} changes will be taken
                      {result.currentVersion
                        ? ` (this proxy is at version ${result.currentVersion} here)`
                        : ""}
                    </span>
                    <Button size="xs" variant="ghost" onClick={() => setAll("incoming")}>
                      Take all incoming
                    </Button>
                    <Button size="xs" variant="ghost" onClick={() => setAll("mine")}>
                      Keep all mine
                    </Button>
                    {result.mineJson && result.incomingJson ? (
                      <Button size="xs" variant="ghost" onClick={() => setShowJson((v) => !v)}>
                        {showJson ? "Hide JSON" : "Compare JSON"}
                      </Button>
                    ) : null}
                  </div>

                  {changes.map((change) => (
                    <ImportChangeRow
                      key={change.field}
                      label={change.label}
                      value={choices[change.field] ?? "mine"}
                      onChange={(choice) =>
                        setChoices((current) => ({ ...current, [change.field]: choice }))
                      }
                    >
                      <ImportValuePair mine={change.mine} incoming={change.incoming} />
                    </ImportChangeRow>
                  ))}

                  {showJson && result.mineJson && result.incomingJson ? (
                    <div className="overflow-hidden rounded-lg border">
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
                        Left: mine (this environment). Right: incoming (the file). Hidden values
                        show as ***.
                      </p>
                    </div>
                  ) : null}
                </div>
              ) : null}
            </div>
          ) : null}

          {applyError ? (
            <Alert variant="destructive" role="alert">
              <AlertTriangle className="h-4 w-4" />
              <AlertDescription className="text-xs">{applyError}</AlertDescription>
            </Alert>
          ) : null}

          {done && result ? (
            <div className="flex flex-col gap-3" data-testid="proxy-import-result">
              <div className="flex items-center gap-2">
                <CheckCircle2 className="h-5 w-5 text-green-500" aria-hidden="true" />
                <span className="text-sm font-semibold">
                  {!done.applied
                    ? `"${result.name}" already matches the file`
                    : done.status === "new"
                      ? `Imported "${result.name}"`
                      : `Updated "${result.name}"`}
                </span>
              </div>
              <ImportSecretChecklist
                missing={result.missingSecrets}
                duplicate={result.duplicateSecrets}
                unchecked={result.secretCheckUnavailable}
              />
            </div>
          ) : null}
        </div>

        <DialogFooter>
          {done ? (
            <>
              <Button type="button" variant="outline" onClick={close}>
                Close
              </Button>
              <Button
                type="button"
                onClick={() => {
                  const id = done.itemId;
                  close();
                  onImported?.(id);
                }}
              >
                Open proxy
              </Button>
            </>
          ) : (
            <>
              <Button type="button" variant="outline" onClick={close}>
                {result?.status === "same" ? "Close" : "Cancel"}
              </Button>
              <Button type="button" disabled={!canApply || busy} onClick={confirm}>
                {apply.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
                {result?.status === "changed"
                  ? takeCount
                    ? `Apply ${takeCount} ${takeCount === 1 ? "change" : "changes"}`
                    : "Nothing to apply"
                  : "Import"}
              </Button>
            </>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
