import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { DiffEditor } from "@monaco-editor/react";
import { useTheme } from "@seliseblocks/genesis-os/hooks";
import { AlertTriangle, CheckCircle2, Info, Loader2, XCircle } from "lucide-react";
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
import {
  useApplyFunctionImport,
  usePreviewFunctionImport,
} from "../../hooks/use-function-transfer";
import { useDeployFunction } from "../../hooks/use-functions";
import { useGetBuild } from "../../hooks/use-versions";
import { isDeployPending } from "../../types/version.types";
import { BuildProgress } from "../build-progress";
import type {
  FunctionImportChoice,
  FunctionImportField,
  IFunctionExportFile,
  IFunctionImportPreview,
  IFunctionImportResult,
} from "../../types/transfer.types";
import { checkImportFile, importErrorOf, readImportFile } from "../../utils/transfer";
import {
  ImportChangeRow,
  ImportDropZone,
  ImportFileCard,
  ImportSecretChecklist,
  ImportStatusChip,
  ImportSteps,
  ImportValuePair,
} from "./import-parts";

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

const STATUS_TEXT: Record<IFunctionImportPreview["status"], string> = {
  new: "This function is not here yet. Importing creates it with the same id, so its URL is the same.",
  same: "This environment already has exactly this function. There is nothing to import.",
  changed: "This function is already here and some parts differ. Choose what to keep.",
  blocked: "This file cannot be imported here right now.",
};

/**
 * After a write: the automatic build and deploy, watched like the editor's Deploy. A build still
 * running is polled; once it succeeds that exact build is deployed. A failed build does not undo
 * the import — "Deploy again" retries, exactly as the editor's Deploy would.
 */
const DeployProgress = ({ result }: { result: IFunctionImportResult }) => {
  const [buildId, setBuildId] = useState(result.deploy?.buildId ?? undefined);
  const [deployState, setDeployState] = useState(result.deploy?.state);
  const [version, setVersion] = useState(result.deploy?.versionNumber ?? undefined);
  const [deployError, setDeployError] = useState(result.deploy?.error ?? undefined);
  const { data: build } = useGetBuild(deployState === "building" ? buildId : undefined);
  const { mutateAsync: deployAsync, isPending } = useDeployFunction();
  const started = useRef<string | null>(null);

  // A watched build that failed is read straight off the build, not copied into state.
  const buildFailed =
    deployState === "building" && !!buildId && build?.id === buildId && build.status === "Failed";
  const state = buildFailed ? "failed" : deployState;
  const error = buildFailed ? (build?.errorMessage ?? "The build failed.") : deployError;

  const run = (payload: { buildId?: string }) =>
    deployAsync({ functionId: result.functionId, ...payload })
      .then((outcome) => {
        if (isDeployPending(outcome)) {
          setBuildId(outcome.buildId);
          setDeployState("building");
        } else {
          setVersion(outcome.number);
          setDeployState("live");
        }
      })
      .catch((failure: unknown) => {
        setDeployState("failed");
        setDeployError(importErrorOf(failure).message);
      });

  useEffect(() => {
    if (deployState !== "building" || !buildId || build?.id !== buildId) return;
    if (build.status !== "Succeeded" || started.current === buildId) return;
    started.current = buildId;
    void run({ buildId });
    // run is recreated per render; the build's status drives this effect.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [build, buildId, deployState]);

  const retry = () => {
    setDeployError(undefined);
    setBuildId(undefined);
    setDeployState("building");
    void run({});
  };

  if (!state) return null;

  return (
    <div className="flex flex-col gap-2" data-testid="import-deploy">
      <div className="flex items-center gap-2 text-sm">
        {state === "building" && <Loader2 className="h-4 w-4 animate-spin text-primary" />}
        {state === "live" && <CheckCircle2 className="h-4 w-4 text-green-500" />}
        {state === "failed" && <XCircle className="h-4 w-4 text-error" />}
        <span>
          {state === "building" && "Imported. Building and deploying…"}
          {state === "live" && `Imported and live${version ? ` as v${version}` : ""}.`}
          {state === "failed" && "Imported, but the deploy failed. The function is saved."}
        </span>
      </div>
      {buildId && <BuildProgress buildId={buildId} />}
      {state === "failed" && (
        <div className="flex flex-wrap items-center gap-2">
          {error && <span className="text-xs text-error">{error}</span>}
          <Button size="sm" variant="outline" disabled={isPending} onClick={retry}>
            {isPending && <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />}
            Deploy again
          </Button>
        </div>
      )}
    </div>
  );
};

/**
 * Import one function from an export file. The preview writes nothing; Apply sends the same file
 * back and the server checks everything again, so what this dialog shows is advice only.
 */
export const FunctionImportDialog = ({ open, onOpenChange }: Props) => {
  const inputRef = useRef<HTMLInputElement>(null);
  // Bumped on every pick and every reset: an answer that arrives for an older file is dropped.
  const ticket = useRef(0);
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { resolvedTheme } = useTheme();
  const [file, setFile] = useState<IFunctionExportFile | null>(null);
  const [picked, setPicked] = useState<{ name: string; size: number } | null>(null);
  const [checking, setChecking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [preview, setPreview] = useState<IFunctionImportPreview | null>(null);
  const [choices, setChoices] = useState<
    Partial<Record<FunctionImportField, FunctionImportChoice>>
  >({});
  const [result, setResult] = useState<IFunctionImportResult | null>(null);
  const previewMutation = usePreviewFunctionImport();
  const applyMutation = useApplyFunctionImport();

  const reset = () => {
    ticket.current += 1;
    setFile(null);
    setPicked(null);
    setChecking(false);
    setError(null);
    setPreview(null);
    setChoices({});
    setResult(null);
  };

  const close = () => {
    reset();
    onOpenChange(false);
  };

  /** Every difference starts as "take the file": the user opened Import to import. */
  const runPreview = async (next: IFunctionExportFile, mine = ticket.current) => {
    setError(null);
    try {
      const answer = await previewMutation.mutateAsync(next);
      if (mine !== ticket.current) return;
      setPreview(answer);
      setChoices(
        Object.fromEntries(
          [...answer.fields, ...answer.files].map((d) => [d.field, "incoming" as const]),
        ),
      );
    } catch (failure) {
      if (mine !== ticket.current) return;
      setPreview(null);
      setError(importErrorOf(failure).message);
    }
  };

  /** One file in, from the picker or a drop: refuse what is clearly wrong, then preview. */
  const pick = async (files: File[]) => {
    reset();
    const mine = ticket.current;
    const chosen = files[0];
    if (!chosen) return;
    if (files.length > 1) {
      setError("Choose one file at a time.");
      return;
    }
    // The cheap checks run before the file is read, so a wrong file never costs a read.
    const refusal = checkImportFile(chosen);
    if (refusal) {
      setError(refusal);
      return;
    }
    setPicked({ name: chosen.name, size: chosen.size });
    setChecking(true);
    try {
      const read = await readImportFile(chosen);
      if (mine !== ticket.current) return;
      setFile(read);
      setChecking(false);
      await runPreview(read, mine);
    } catch (failure) {
      if (mine !== ticket.current) return;
      setChecking(false);
      setPicked(null);
      setError(failure instanceof Error ? failure.message : "The file could not be read.");
    }
  };

  const diffs = preview ? [...preview.fields, ...preview.files] : [];
  const takeCount = diffs.filter((d) => choices[d.field] === "incoming").length;
  const canApply =
    !!file &&
    !!preview &&
    !result &&
    (preview.status === "new" || (preview.status === "changed" && takeCount > 0));

  const setAll = (choice: FunctionImportChoice) =>
    setChoices(Object.fromEntries(diffs.map((d) => [d.field, choice])));

  const choose = (field: FunctionImportField, choice: FunctionImportChoice) =>
    setChoices((all) => ({ ...all, [field]: choice }));

  const apply = async () => {
    if (!file || !preview) return;
    setError(null);
    try {
      const done = await applyMutation.mutateAsync({
        file,
        resolution: preview.status === "changed" ? "merge" : undefined,
        fieldChoices: preview.status === "changed" ? choices : undefined,
        expectedLastUpdatedDate: preview.targetLastUpdatedDate ?? undefined,
      });
      setResult(done);
      showSuccessToast({
        description:
          done.status === "created"
            ? `Imported "${done.name}".`
            : done.status === "updated"
              ? `Updated "${done.name}" from the file.`
              : "Nothing changed: the function already matches.",
      });
    } catch (failure) {
      const { status, message } = importErrorOf(failure);
      if (status === 409) {
        // Someone changed it meanwhile (or it is hidden / still being deleted): show the fresh state.
        showErrorToast({ errors: `${message} The preview was refreshed.` });
        await runPreview(file);
        return;
      }
      setError(message);
    }
  };

  const busy = checking || previewMutation.isPending || applyMutation.isPending;
  const step = result ? 2 : preview ? 1 : 0;

  return (
    <Dialog open={open} onOpenChange={(next) => (next ? onOpenChange(true) : close())}>
      <DialogContent className="max-h-[90vh] w-[min(96vw,1100px)] max-w-none overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Import function</DialogTitle>
          <DialogDescription>
            Choose a function export file (.json). Nothing changes until you press Import.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <ImportSteps current={step} />

          <input
            ref={inputRef}
            type="file"
            accept=".json,application/json"
            className="hidden"
            aria-label="Function export file"
            data-testid="function-import-input"
            onChange={(e) => {
              void pick([...(e.target.files ?? [])]);
              e.target.value = "";
            }}
          />

          {!picked && !result && (
            <ImportDropZone
              noun="function"
              disabled={busy}
              onBrowse={() => inputRef.current?.click()}
              onDropFiles={(files) => void pick(files)}
            />
          )}

          {picked && !result && (
            <ImportFileCard
              name={picked.name}
              size={picked.size}
              checking={checking || previewMutation.isPending}
              disabled={applyMutation.isPending}
              onReplace={() => inputRef.current?.click()}
              onRemove={reset}
            />
          )}

          {error && (
            <Alert variant="destructive" role="alert">
              <AlertTriangle className="h-4 w-4" />
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}

          {preview && !result && (
            <>
              <div className="flex flex-col gap-1.5">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-sm font-semibold">{preview.name}</span>
                  <ImportStatusChip status={preview.status} />
                  <span className="font-mono text-xs font-normal text-medium-emphasis">
                    {preview.functionId}
                  </span>
                </div>
                <span className="text-sm" data-testid="import-status">
                  {STATUS_TEXT[preview.status]}
                </span>
                {preview.blockedReason && (
                  <span className="text-sm text-error">{preview.blockedReason}</span>
                )}
              </div>

              {preview.warnings.length > 0 && (
                <Alert>
                  <Info className="h-4 w-4" />
                  <AlertDescription>
                    <ul className="list-disc space-y-1 pl-4">
                      {preview.warnings.map((w) => (
                        <li key={w}>{w}</li>
                      ))}
                    </ul>
                  </AlertDescription>
                </Alert>
              )}

              <ImportSecretChecklist
                missing={preview.missingSecrets}
                duplicate={preview.duplicateSecrets}
                unresolved={preview.unresolvedSecretRefs}
                unchecked={preview.secretsUnchecked}
              />

              {preview.status === "changed" && (
                <div className="flex flex-col gap-3">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-sm text-medium-emphasis" aria-live="polite">
                      {takeCount} of {diffs.length} changes will be taken
                    </span>
                    <Button size="xs" variant="ghost" onClick={() => setAll("incoming")}>
                      Take all incoming
                    </Button>
                    <Button size="xs" variant="ghost" onClick={() => setAll("mine")}>
                      Keep all mine
                    </Button>
                  </div>

                  {preview.fields.map((f) => (
                    <ImportChangeRow
                      key={f.field}
                      label={f.label}
                      value={choices[f.field] ?? "mine"}
                      onChange={(c) => choose(f.field, c)}
                    >
                      <ImportValuePair mine={f.mine} incoming={f.incoming} />
                    </ImportChangeRow>
                  ))}

                  {preview.files.map((f) => (
                    <ImportChangeRow
                      key={f.field}
                      label={f.file}
                      value={choices[f.field] ?? "mine"}
                      onChange={(c) => choose(f.field, c)}
                    >
                      <p className="mb-2 text-xs text-medium-emphasis">
                        Left: mine (here now). Right: incoming (file).
                      </p>
                      <DiffEditor
                        height="280px"
                        language={f.language}
                        theme={resolvedTheme === "dark" ? "vs-dark" : "vs"}
                        original={f.mine ?? ""}
                        modified={f.incoming ?? ""}
                        options={{
                          readOnly: true,
                          renderSideBySide: true,
                          minimap: { enabled: false },
                          scrollBeyondLastLine: false,
                          automaticLayout: true,
                          fontSize: 12,
                        }}
                      />
                    </ImportChangeRow>
                  ))}
                </div>
              )}
            </>
          )}

          {result && (
            <div className="flex flex-col gap-3" data-testid="import-result">
              <div className="flex items-center gap-2">
                <CheckCircle2 className="h-5 w-5 text-green-500" aria-hidden="true" />
                <span className="text-sm font-semibold">
                  {result.status === "created"
                    ? `Imported "${result.name}"`
                    : result.status === "updated"
                      ? `Updated "${result.name}"`
                      : `"${result.name}" already matches the file`}
                </span>
              </div>
              {result.messages.map((m) => (
                <Alert key={m}>
                  <Info className="h-4 w-4" />
                  <AlertDescription>{m}</AlertDescription>
                </Alert>
              ))}
              <ImportSecretChecklist
                missing={result.missingSecrets}
                duplicate={result.duplicateSecrets}
              />
              {result.deploy && <DeployProgress result={result} />}
            </div>
          )}
        </div>

        <DialogFooter className="gap-2">
          {result ? (
            <>
              <Button variant="outline" onClick={close}>
                Close
              </Button>
              <Button
                onClick={() => {
                  const id = result.functionId;
                  close();
                  navigate(scoped(`functions/${id}`));
                }}
              >
                Open function
              </Button>
            </>
          ) : (
            <>
              <Button variant="outline" onClick={close}>
                Cancel
              </Button>
              <Button disabled={!canApply || busy} onClick={() => void apply()}>
                {applyMutation.isPending && <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />}
                Import
              </Button>
            </>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
