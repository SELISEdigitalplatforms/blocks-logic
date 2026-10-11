import { Fragment, useEffect, useMemo, useRef, useState } from "react";
import { ArrowUpCircle, Check, KeyRound, Loader2, Plug, TriangleAlert } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { CONNECTION_PRESETS, IConnectionPreset } from "../../constants/connections.constant";
import { IVariableBinding } from "../../types/function.types";
import {
  addMissingVariables,
  applyPreset,
  checkSetup,
  fillKnownValues,
  findUpdate,
  getDependencies,
  pinVersion,
  presetForPackage,
  presetStatus,
  resolveLatestVersion,
} from "../../utils/connections";

type ConnectionsCardProps = {
  indexJs: string;
  packageJson: string;
  variables: IVariableBinding[];
  onPackageJsonChange: (value: string) => void;
  onVariablesChange: (value: IVariableBinding[]) => void;
  onEditVariables: () => void;
  /** The project's API host (`blocksapi.<domain>` or the shared one), prefilled into BLOCKS_API_URL. */
  blocksApiHost?: string;
};

/**
 * The services a function talks to, with what each one needs: a pinned package in package.json
 * and the variables its connection reads. Also checks the code against its own setup, so an
 * import with no package or a `ctx.env` key with no variable shows up before a deploy fails.
 */
export const ConnectionsCard = ({
  indexJs,
  packageJson,
  variables,
  onPackageJsonChange,
  onVariablesChange,
  onEditVariables,
  blocksApiHost,
}: ConnectionsCardProps) => {
  const [openId, setOpenId] = useState<string | null>(null);
  const [pendingId, setPendingId] = useState<string | null>(null);
  // The npm lookup is async: merge into what the editor holds when it answers, not what it held
  // when the button was clicked, so anything typed meanwhile is kept.
  const latest = useRef({ packageJson, variables });
  useEffect(() => {
    latest.current = { packageJson, variables };
  }, [packageJson, variables]);
  const dependencies = useMemo(() => getDependencies(packageJson), [packageJson]);
  const setup = useMemo(
    () => checkSetup(indexJs, packageJson, variables),
    [indexJs, packageJson, variables],
  );
  const fillable = useMemo(
    () => fillKnownValues(variables, { blocksApiHost }),
    [variables, blocksApiHost],
  );
  const open = CONNECTION_PRESETS.find((preset) => preset.id === openId) ?? null;

  // npm's latest stable for each catalog package the function already pins exactly, so an added
  // service shows when a newer release exists. Asked once per package per mount; an npm failure
  // just means no update is offered — the tested fallback is never shown as "latest".
  const [latestVersions, setLatestVersions] = useState<Record<string, string>>({});
  const asked = useRef(new Set<string>());
  // Per mount, not per effect run: package.json changes on every keystroke, and an answer that
  // lands after such a change is still the right answer.
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);
  useEffect(() => {
    for (const preset of CONNECTION_PRESETS) {
      if (!(preset.packageName in dependencies) || asked.current.has(preset.packageName)) continue;
      asked.current.add(preset.packageName);
      void resolveLatestVersion(preset).then((resolved) => {
        if (mounted.current && resolved.source === "npm") {
          setLatestVersions((prev) => ({ ...prev, [preset.packageName]: resolved.version }));
        }
      });
    }
  }, [dependencies]);
  const updateFor = (preset: IConnectionPreset) =>
    findUpdate(dependencies[preset.packageName], latestVersions[preset.packageName]);

  const update = (preset: IConnectionPreset) => {
    const target = updateFor(preset);
    if (!target) return;
    const result = pinVersion(latest.current.packageJson, preset.packageName, target.to);
    if (!result.ok) {
      showErrorToast({ title: `Could not update ${preset.packageName}`, errors: [result.reason] });
      return;
    }
    onPackageJsonChange(result.packageJson);
    showSuccessToast({
      title: `${preset.packageName} updated`,
      description: `${target.from} → ${target.to}.${
        target.breaking ? " This is a breaking release — check its changelog and run a test." : ""
      } Save to keep it.`,
    });
  };

  const add = async (preset: IConnectionPreset): Promise<boolean> => {
    if (pendingId) return false;
    // Only ask npm when the package would actually be added — a listed one keeps its version.
    const listed = preset.packageName in getDependencies(latest.current.packageJson);
    let resolved: Awaited<ReturnType<typeof resolveLatestVersion>> | null = null;
    if (!listed) {
      setPendingId(preset.id);
      try {
        resolved = await resolveLatestVersion(preset);
      } finally {
        setPendingId(null);
      }
    }
    const result = applyPreset(
      preset,
      latest.current.packageJson,
      latest.current.variables,
      { blocksApiHost },
      resolved?.version,
    );
    if (!result.ok) {
      showErrorToast({ title: `Could not add ${preset.label}`, errors: [result.reason] });
      return false;
    }
    if (result.addedPackage) onPackageJsonChange(result.packageJson);
    if (result.addedKeys.length > 0 || result.filledKeys.length > 0) {
      onVariablesChange(result.variables);
    }

    const secrets = preset.variables.filter(
      (variable) => variable.secret && result.addedKeys.includes(variable.key),
    );
    const parts = [
      result.addedPackage
        ? `Added ${preset.packageName}@${result.version}${
            resolved?.source === "fallback" ? " (npm was unreachable, so the tested version)" : ""
          }.`
        : `${preset.packageName} was already listed (${result.existingVersion}) and was left as is.`,
      result.addedKeys.length > 0 ? `Added ${result.addedKeys.join(", ")}.` : null,
      result.filledKeys.length > 0 ? `Filled in ${result.filledKeys.join(", ")}.` : null,
      secrets.length > 0
        ? `Bind ${secrets.map((variable) => variable.key).join(", ")} to a configuration variable before deploying.`
        : null,
    ];
    showSuccessToast({
      title: `${preset.label} set up`,
      description: parts.filter(Boolean).join(" "),
    });
    return true;
  };

  const hasIssues =
    !setup.manifestValid ||
    setup.missingPackages.length > 0 ||
    setup.missingVariables.length > 0 ||
    setup.emptyVariables.length > 0;

  return (
    <Card>
      <CardContent className="flex flex-col gap-3 p-4">
        <div className="flex flex-col gap-0.5">
          <span className="flex items-center gap-2 text-sm font-semibold">
            <Plug className="h-3.5 w-3.5" />
            Connections
          </span>
          <span className="text-xs leading-relaxed text-medium-emphasis">
            Adds the package and the variables a service needs.
          </span>
        </div>

        {hasIssues && (
          <div
            role="status"
            aria-label="Setup check"
            className="flex flex-col gap-2 rounded-md border border-warning-200 bg-warning-50 p-2.5 text-xs"
          >
            {!setup.manifestValid && (
              <SetupIssue>
                <code className="font-mono">package.json</code> is not valid JSON.
              </SetupIssue>
            )}
            {setup.missingPackages.map((name) => {
              const preset = presetForPackage(name);
              return (
                <SetupIssue key={name}>
                  <span className="min-w-0 [overflow-wrap:anywhere]">
                    <code className="font-mono">{name}</code> is imported but not in package.json.
                  </span>
                  {preset ? (
                    <button
                      type="button"
                      className="font-semibold text-primary hover:underline"
                      onClick={() => void add(preset)}
                      disabled={pendingId !== null}
                    >
                      {pendingId === preset.id ? "Checking npm…" : `Add ${name}`}
                    </button>
                  ) : (
                    <span className="text-medium-emphasis">Add it with an exact version.</span>
                  )}
                </SetupIssue>
              );
            })}
            {setup.missingVariables.length > 0 && (
              <SetupIssue>
                <span className="min-w-0 [overflow-wrap:anywhere]">
                  {setup.missingVariables.map((key) => (
                    <Fragment key={key}>
                      <code className="font-mono">ctx.env.{key}</code>{" "}
                    </Fragment>
                  ))}
                  {setup.missingVariables.length === 1 ? "is" : "are"} read but not defined.
                </span>
                <button
                  type="button"
                  className="font-semibold text-primary hover:underline"
                  onClick={() =>
                    onVariablesChange(addMissingVariables(variables, setup.missingVariables))
                  }
                >
                  Add {setup.missingVariables.length === 1 ? "variable" : "variables"}
                </button>
              </SetupIssue>
            )}
            {setup.emptyVariables.length > 0 && (
              <SetupIssue>
                <span className="min-w-0 [overflow-wrap:anywhere]">
                  {setup.emptyVariables.map((key) => (
                    <Fragment key={key}>
                      <code className="font-mono">{key}</code>{" "}
                    </Fragment>
                  ))}
                  {setup.emptyVariables.length === 1 ? "has" : "have"} no value.
                </span>
                {fillable.filledKeys.length > 0 && (
                  <button
                    type="button"
                    className="font-semibold text-primary hover:underline"
                    onClick={() => onVariablesChange(fillable.variables)}
                  >
                    Fill in {fillable.filledKeys.join(", ")}
                  </button>
                )}
                <button
                  type="button"
                  className="font-semibold text-primary hover:underline"
                  onClick={onEditVariables}
                >
                  Edit variables ›
                </button>
              </SetupIssue>
            )}
          </div>
        )}

        <div className="flex flex-col">
          {CONNECTION_PRESETS.map((preset) => {
            const status = presetStatus(preset, dependencies, variables);
            // A missing variable matters more than a newer release, so "Incomplete" wins.
            const available = status === "added" ? updateFor(preset) : null;
            return (
              <button
                key={preset.id}
                type="button"
                className="flex items-center justify-between gap-3 border-b py-2 text-left last:border-b-0 hover:bg-surface-app"
                onClick={() => setOpenId(preset.id)}
                aria-label={`${preset.label}: ${
                  available ? `update to ${available.to} available` : STATUS_LABEL[status]
                }`}
              >
                <span className="flex min-w-0 flex-col">
                  <span className="text-xs font-semibold">{preset.label}</span>
                  <code className="truncate font-mono text-[11px] text-low-emphasis">
                    {preset.packageName}
                  </code>
                </span>
                {available ? (
                  <Badge
                    variant="info"
                    className="gap-1"
                    title={`${available.from} → ${available.to}`}
                  >
                    <ArrowUpCircle className="h-3 w-3" />
                    Update
                  </Badge>
                ) : status === "added" ? (
                  <Badge variant="success" className="gap-1">
                    <Check className="h-3 w-3" />
                    Added
                  </Badge>
                ) : status === "partial" ? (
                  <Badge variant="warning">Incomplete</Badge>
                ) : (
                  <span className="text-xs font-semibold text-primary">Set up ›</span>
                )}
              </button>
            );
          })}
        </div>

        <p className="text-xs leading-relaxed text-low-emphasis">
          Only public endpoints are reachable — private networks and the VPN are blocked. The
          examples connect and close inside one call to stay short; in your function, create the
          client once at module level and reuse it.
        </p>
      </CardContent>

      <Dialog open={open !== null} onOpenChange={(isOpen) => !isOpen && setOpenId(null)}>
        {open && (
          <ConnectionDialogBody
            preset={open}
            dependencies={dependencies}
            variables={variables}
            update={updateFor(open)}
            onUpdate={() => update(open)}
            onAdd={async () => {
              if (await add(open)) setOpenId(null);
            }}
            isAdding={pendingId === open.id}
            blocksApiHost={blocksApiHost}
          />
        )}
      </Dialog>
    </Card>
  );
};

const STATUS_LABEL = { added: "added", partial: "incomplete", none: "not set up" } as const;

const SetupIssue = ({ children }: { children: React.ReactNode }) => (
  <div className="flex items-start gap-2">
    <TriangleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0 text-warning-700" />
    <div className="flex min-w-0 flex-wrap items-baseline gap-x-2 gap-y-0.5">{children}</div>
  </div>
);

type ConnectionDialogBodyProps = {
  preset: IConnectionPreset;
  dependencies: Record<string, string>;
  variables: IVariableBinding[];
  /** A newer stable release than the exact version package.json pins. */
  update: ReturnType<typeof findUpdate>;
  onUpdate: () => void;
  onAdd: () => void;
  isAdding: boolean;
  blocksApiHost?: string;
};

const ConnectionDialogBody = ({
  preset,
  dependencies,
  variables,
  update,
  onUpdate,
  onAdd,
  isAdding,
  blocksApiHost,
}: ConnectionDialogBodyProps) => {
  const [copied, setCopied] = useState(false);
  const status = presetStatus(preset, dependencies, variables);
  const keys = new Set(variables.map((variable) => variable.key));
  /** The value adding would give this row: only when it is missing or blank, never over a typed one. */
  const fillsTo = (variable: IConnectionPreset["variables"][number]) =>
    variables.some((row) => row.key === variable.key && row.value.trim() !== "")
      ? ""
      : fillKnownValues([{ key: variable.key, value: "" }], { blocksApiHost }).variables[0].value;
  const listed = dependencies[preset.packageName];

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(preset.snippet);
      setCopied(true);
      setTimeout(() => setCopied(false), 1400);
    } catch {
      showErrorToast({
        title: "Could not copy",
        errors: ["Select the example and copy it by hand."],
      });
    }
  };

  return (
    <DialogContent className="max-h-[90vh] max-w-2xl overflow-y-auto">
      <DialogHeader>
        <DialogTitle>{preset.label}</DialogTitle>
        <DialogDescription>{preset.description}</DialogDescription>
      </DialogHeader>

      <div className="flex flex-col gap-1.5">
        <span className="text-xs font-semibold uppercase tracking-wide text-low-emphasis">
          Package
        </span>
        <div className="flex items-center justify-between gap-3 rounded-md border px-3 py-2">
          <code className="font-mono text-xs">
            {preset.packageName}@{listed ?? "latest"}
          </code>
          {update ? (
            <span className="flex items-center gap-2">
              <span className="text-xs text-medium-emphasis">{update.to} is the latest</span>
              <Button type="button" size="sm" variant="outline" onClick={onUpdate}>
                Update to {update.to}
              </Button>
            </span>
          ) : listed !== undefined ? (
            <span className="text-xs text-medium-emphasis">in package.json — left as is</span>
          ) : (
            <span className="text-xs text-medium-emphasis">
              today&apos;s latest, pinned exactly when added
            </span>
          )}
        </div>
        {update?.breaking && (
          <span className="flex items-start gap-1.5 text-xs text-warning-800">
            <TriangleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            {update.to} is a breaking release. Check its changelog and run a test before deploying.
          </span>
        )}
      </div>

      <div className="flex flex-col gap-1.5">
        <span className="text-xs font-semibold uppercase tracking-wide text-low-emphasis">
          Variables
        </span>
        <div className="flex flex-col rounded-md border">
          {preset.variables.map((variable) => (
            <div
              key={variable.key}
              className="flex flex-col gap-0.5 border-b px-3 py-2 last:border-b-0"
            >
              <span className="flex flex-wrap items-center gap-2">
                <code className="font-mono text-xs font-semibold">{variable.key}</code>
                {variable.secret && (
                  <Badge variant="warning" className="gap-1">
                    <KeyRound className="h-3 w-3" />
                    Bind to a secret
                  </Badge>
                )}
                {keys.has(variable.key) && (
                  <span className="text-xs text-medium-emphasis">already defined</span>
                )}
              </span>
              <span className="break-all text-xs text-medium-emphasis">{variable.hint}</span>
              {fillsTo(variable) && (
                <span className="break-all text-xs">
                  Will be set to <code className="font-mono">{fillsTo(variable)}</code>
                </span>
              )}
            </div>
          ))}
        </div>
      </div>

      <div className="flex flex-col gap-1.5">
        <div className="flex items-center justify-between">
          <span className="text-xs font-semibold uppercase tracking-wide text-low-emphasis">
            Example
          </span>
          <button
            type="button"
            className="text-xs font-semibold text-primary hover:underline"
            onClick={copy}
          >
            {copied ? "Copied ✓" : "Copy"}
          </button>
        </div>
        <pre className="overflow-x-auto rounded-md bg-surface-app px-3 py-2.5 font-mono text-xs leading-relaxed">
          {preset.snippet}
        </pre>
      </div>

      <DialogFooter>
        <Button
          type="button"
          onClick={onAdd}
          disabled={status === "added" || isAdding}
          className="gap-1.5"
        >
          {isAdding && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
          {status === "added" ? "Already set up" : isAdding ? "Checking npm…" : "Add to function"}
        </Button>
      </DialogFooter>
    </DialogContent>
  );
};
