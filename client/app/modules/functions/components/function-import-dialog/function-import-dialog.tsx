import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { DiffEditor } from "@monaco-editor/react";
import { useTheme } from "@seliseblocks/genesis-os/hooks";
import { AlertTriangle, CheckCircle2, FileUp, Info, Loader2, XCircle } from "lucide-react";
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
import { cn } from "@/lib/utils";
import { useApplyFunctionImport, usePreviewFunctionImport } from "../../hooks/use-function-transfer";
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
import { importErrorOf, readImportFile } from "../../utils/transfer";

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

/** One keep / take toggle. */
const ChoiceToggle = ({
  label,
  value,
  onChange,
}: {
  label: string;
  value: FunctionImportChoice;
  onChange: (value: FunctionImportChoice) => void;
}) => (
  <div role="group" aria-label={`${label}: keep or take`} className="inline-flex rounded-md border">
    {(["mine", "incoming"] as const).map((choice) => (
      <button
        key={choice}
        type="button"
        aria-pressed={value === choice}
        className={cn(
          "px-2.5 py-1 text-xs font-medium first:rounded-l-md last:rounded-r-md",
          value === choice ? "bg-primary text-white" : "text-medium-emphasis hover:bg-surface-app",
        )}
        onClick={() => onChange(choice)}
      >
        {choice === "mine" ? "Keep mine" : "Take file"}
      </button>
    ))}
  </div>
);

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
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { resolvedTheme } = useTheme();
  const [file, setFile] = useState<IFunctionExportFile | null>(null);
  const [fileName, setFileName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [preview, setPreview] = useState<IFunctionImportPreview | null>(null);
  const [choices, setChoices] = useState<Partial<Record<FunctionImportField, FunctionImportChoice>>>({});
  const [result, setResult] = useState<IFunctionImportResult | null>(null);
  const previewMutation = usePreviewFunctionImport();
  const applyMutation = useApplyFunctionImport();

  const reset = () => {
    setFile(null);
    setFileName("");
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
  const runPreview = async (next: IFunctionExportFile) => {
    setError(null);
    try {
      const answer = await previewMutation.mutateAsync(next);
      setPreview(answer);
      setChoices(
        Object.fromEntries(
          [...answer.fields, ...answer.files].map((d) => [d.field, "incoming" as const]),
        ),
      );
    } catch (failure) {
      setPreview(null);
      setError(importErrorOf(failure).message);
    }
  };

  const pick = async (picked: File | undefined) => {
    reset();
    if (!picked) return;
    setFileName(picked.name);
    try {
      const read = await readImportFile(picked);
      setFile(read);
      await runPreview(read);
    } catch (failure) {
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

  const busy = previewMutation.isPending || applyMutation.isPending;

  return (
    <Dialog open={open} onOpenChange={(next) => (next ? onOpenChange(true) : close())}>
      <DialogContent className="max-h-[90vh] w-[min(96vw,1100px)] max-w-none overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Import function</DialogTitle>
          <DialogDescription>
            Choose a function export file. Nothing changes until you press Import.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <div className="flex flex-wrap items-center gap-3">
            <input
              ref={inputRef}
              type="file"
              accept="application/json,.json"
              className="hidden"
              aria-label="Function export file"
              data-testid="function-import-input"
              onChange={(e) => {
                void pick(e.target.files?.[0]);
                e.target.value = "";
              }}
            />
            <Button
              variant="outline"
              size="sm"
              className="gap-1.5"
              disabled={busy}
              onClick={() => inputRef.current?.click()}
            >
              <FileUp className="h-4 w-4" />
              Choose file
            </Button>
            {fileName && <span className="truncate text-sm text-medium-emphasis">{fileName}</span>}
            {previewMutation.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
          </div>

          {error && (
            <Alert variant="destructive">
              <AlertTriangle className="h-4 w-4" />
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}

          {preview && !result && (
            <>
              <div className="flex flex-col gap-1">
                <span className="text-sm font-semibold">
                  {preview.name}{" "}
                  <span className="font-mono text-xs font-normal text-medium-emphasis">
                    {preview.functionId}
                  </span>
                </span>
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

              {preview.status === "changed" && (
                <div className="flex flex-col gap-3">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-sm text-medium-emphasis">
                      {takeCount} of {diffs.length} differences will be taken from the file.
                    </span>
                    <Button size="sm" variant="ghost" onClick={() => setAll("incoming")}>
                      Take all from file
                    </Button>
                    <Button size="sm" variant="ghost" onClick={() => setAll("mine")}>
                      Keep all mine
                    </Button>
                  </div>

                  {preview.fields.length > 0 && (
                    <div className="overflow-x-auto rounded-md border">
                      <table className="w-full text-left text-xs">
                        <thead className="border-b bg-surface-app text-medium-emphasis">
                          <tr>
                            <th className="px-3 py-2">Setting</th>
                            <th className="px-3 py-2">Here now</th>
                            <th className="px-3 py-2">In the file</th>
                            <th className="px-3 py-2">Use</th>
                          </tr>
                        </thead>
                        <tbody>
                          {preview.fields.map((f) => (
                            <tr key={f.field} className="border-b align-top last:border-b-0">
                              <td className="px-3 py-2 font-medium">{f.label}</td>
                              <td className="px-3 py-2">
                                <pre className="max-h-40 max-w-[320px] overflow-auto whitespace-pre-wrap break-all font-mono">
                                  {f.mine ?? "—"}
                                </pre>
                              </td>
                              <td className="px-3 py-2">
                                <pre className="max-h-40 max-w-[320px] overflow-auto whitespace-pre-wrap break-all font-mono">
                                  {f.incoming ?? "—"}
                                </pre>
                              </td>
                              <td className="px-3 py-2">
                                <ChoiceToggle
                                  label={f.label}
                                  value={choices[f.field] ?? "mine"}
                                  onChange={(c) => setChoices((all) => ({ ...all, [f.field]: c }))}
                                />
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}

                  {preview.files.map((f) => (
                    <div key={f.field} className="flex flex-col gap-2 rounded-md border p-2">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <span className="font-mono text-sm font-semibold">{f.file}</span>
                        <span className="text-xs text-medium-emphasis">left: here now · right: file</span>
                        <ChoiceToggle
                          label={f.file}
                          value={choices[f.field] ?? "mine"}
                          onChange={(c) => setChoices((all) => ({ ...all, [f.field]: c }))}
                        />
                      </div>
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
                    </div>
                  ))}
                </div>
              )}
            </>
          )}

          {result && (
            <div className="flex flex-col gap-3" data-testid="import-result">
              {result.messages.map((m) => (
                <Alert key={m}>
                  <Info className="h-4 w-4" />
                  <AlertDescription>{m}</AlertDescription>
                </Alert>
              ))}
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
