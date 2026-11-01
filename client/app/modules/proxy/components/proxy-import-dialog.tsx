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
import { useT, tStatic } from "@seliseblocks/genesis-os";
import { renderRichText } from "@/lib/rich-text";

const LOGIC_PROXY_COMPONENTS_MODULE = "logic.proxy.components";

type Choice = "mine" | "incoming";

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Called after a write, with the imported proxy's id. */
  onImported?: (proxyId: string) => void;
};

const VERSION_CONFLICT = "PROXY_VERSION_CONFLICT";

const STATUS_TEXT: Record<ProxyImportResult["status"], string> = {
  get new() {
    return tStatic(LOGIC_PROXY_COMPONENTS_MODULE, "PROXY_IMPORT_DIALOG.NOT_IN_THIS_ENVIRONMENT");
  },
  get same() {
    return tStatic(LOGIC_PROXY_COMPONENTS_MODULE, "PROXY_IMPORT_DIALOG.THIS_ENVIRONMENT_ALREADY_HAS");
  },
  get changed() {
    return tStatic(LOGIC_PROXY_COMPONENTS_MODULE, "PROXY_IMPORT_DIALOG.THIS_PROXY_IS_ALREADY");
  },
  get blocked() {
    return tStatic(LOGIC_PROXY_COMPONENTS_MODULE, "PROXY_IMPORT_DIALOG.THIS_FILE_CANNOT_BE");
  },
};

type Done = { itemId: string; applied: boolean; status: ProxyImportResult["status"] };

const importToastText = (done: Done) => {
  const module = LOGIC_PROXY_COMPONENTS_MODULE;
  if (!done.applied) return tStatic(module, "PROXY_IMPORT_DIALOG.NOTHING_TO_CHANGE_THE");
  if (done.status === "new") return tStatic(module, "PROXY_IMPORT_DIALOG.PROXY_IMPORTED");
  return tStatic(module, "PROXY_IMPORT_DIALOG.PROXY_UPDATED_FROM_THE");
};

const importResultText = (done: Done, name: string) => {
  const module = LOGIC_PROXY_COMPONENTS_MODULE;
  if (!done.applied) return tStatic(module, "PROXY_IMPORT_DIALOG.ALREADY_MATCHES_THE_FILE", { name });
  if (done.status === "new") return tStatic(module, "PROXY_IMPORT_DIALOG.IMPORTED", { name });
  return tStatic(module, "PROXY_IMPORT_DIALOG.UPDATED", { name });
};

const confirmLabel = (status: Done["status"] | undefined, takeCount: number) => {
  const module = LOGIC_PROXY_COMPONENTS_MODULE;
  if (status !== "changed") return tStatic(module, "PROXY_IMPORT_DIALOG.IMPORT");
  if (!takeCount) return tStatic(module, "PROXY_IMPORT_DIALOG.NOTHING_TO_APPLY");
  return `Apply ${takeCount} ${takeCount === 1 ? "change" : "changes"}`;
};

/**
 * Import one proxy from an export file. The preview writes nothing; Apply sends the same file back and
 * the server checks everything again, so what the dialog shows is advice, never the authority.
 */
export const ProxyImportDialog = ({ open, onOpenChange, onImported }: Props) => {
  const { t } = useT(LOGIC_PROXY_COMPONENTS_MODULE);
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
      setReadError(t("PROXY_IMPORT_DIALOG.CHOOSE_ONE_FILE_AT"));
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
        description: importToastText({ itemId: written.itemId, applied: !!written.applied, status }),
      });
      setDone({ itemId: written.itemId, applied: !!written.applied, status });
      return;
    }

    if (response.code === VERSION_CONFLICT) {
      // Someone saved meanwhile: show the fresh difference instead of applying old choices to it.
      showErrorToast({
        errors: t("PROXY_IMPORT_DIALOG.THIS_PROXY_CHANGED_WHILE"),
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
          <DialogTitle>{t("PROXY_IMPORT_DIALOG.IMPORT_PROXY")}</DialogTitle>
          <DialogDescription>{t("PROXY_IMPORT_DIALOG.CHOOSE_A_PROXY_EXPORT")}</DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <ImportSteps current={step} />

          <input
            ref={inputRef}
            type="file"
            accept=".json,application/json"
            className="hidden"
            aria-label={t("PROXY_IMPORT_DIALOG.PROXY_FILE")}
            onChange={(event) => {
              void pick([...(event.target.files ?? [])]);
              event.target.value = "";
            }}
          />

          {!picked && !done ? (
            <ImportDropZone
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
                    {result.reason ?? t("PROXY_IMPORT_DIALOG.THIS_FILE_CANNOT_BE")}
                  </AlertDescription>
                </Alert>
              ) : null}

              {result.status === "new" && result.historyExists ? (
                <Alert>
                  <Info className="h-4 w-4" />
                  <AlertDescription className="text-xs">
                    {t("PROXY_IMPORT_DIALOG.A_PROXY_WITH_THIS", {
                      currentVersion: result.currentVersion ?? "",
                    })}
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
                    {renderRichText(
                      t("PROXY_IMPORT_DIALOG.FILE_HIDES_VALUES", {
                        count: result.maskedValues.length,
                        names: result.maskedValues.join(", "),
                      }),
                      [
                        (text) => <span className="font-mono">{text}</span>,
                        (text) => <span className="font-mono">{text}</span>,
                        (text) => <span className="font-mono">{text}</span>,
                        (text) => <span className="font-mono">{text}</span>,
                      ],
                    )}
                  </AlertDescription>
                </Alert>
              ) : null}

              {result.status === "changed" && changes.length ? (
                <div className="flex flex-col gap-3">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-sm text-medium-emphasis" aria-live="polite">
                      {t("PROXY_IMPORT_DIALOG.OF_CHANGES_WILL_BE", {
                        takeCount,
                        length: changes.length,
                      })}
                      {result.currentVersion
                        ? t("PROXY_IMPORT_DIALOG.THIS_PROXY_IS_AT", {
                            currentVersion: result.currentVersion,
                          })
                        : ""}
                    </span>
                    <Button size="xs" variant="ghost" onClick={() => setAll("incoming")}>
                      {t("PROXY_IMPORT_DIALOG.TAKE_ALL_INCOMING")}
                    </Button>
                    <Button size="xs" variant="ghost" onClick={() => setAll("mine")}>
                      {t("PROXY_IMPORT_DIALOG.KEEP_ALL_MINE")}
                    </Button>
                    {result.mineJson && result.incomingJson ? (
                      <Button size="xs" variant="ghost" onClick={() => setShowJson((v) => !v)}>
                        {showJson
                          ? t("PROXY_IMPORT_DIALOG.HIDE_JSON")
                          : t("PROXY_IMPORT_DIALOG.COMPARE_JSON")}
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
                        {t("PROXY_IMPORT_DIALOG.LEFT_MINE_THIS_ENVIRONMENT")}
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
                  {importResultText(done, result.name)}
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
                {t("common:CLOSE")}
              </Button>
              <Button
                type="button"
                onClick={() => {
                  const id = done.itemId;
                  close();
                  onImported?.(id);
                }}
              >
                {t("PROXY_IMPORT_DIALOG.OPEN_PROXY")}
              </Button>
            </>
          ) : (
            <>
              <Button type="button" variant="outline" onClick={close}>
                {result?.status === "same" ? t("common:CLOSE") : t("common:CANCEL")}
              </Button>
              <Button type="button" disabled={!canApply || busy} onClick={confirm}>
                {apply.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
                {confirmLabel(result?.status, takeCount)}
              </Button>
            </>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
