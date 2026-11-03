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
import { renderRichText } from "@/lib/rich-text";
import { useT, tStatic } from "@seliseblocks/genesis-os";

const LOGIC_FUNCTIONS_COMPONENTS_MODULE = "logic.functions.components";

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
  const { t } = useT(LOGIC_FUNCTIONS_COMPONENTS_MODULE);
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
      showErrorToast({
        title: t("CONNECTIONS_CARD.COULD_NOT_UPDATE", { packageName: preset.packageName }),
        errors: [result.reason],
      });
      return;
    }
    onPackageJsonChange(result.packageJson);
    showSuccessToast({
      title: t("CONNECTIONS_CARD.UPDATED", { packageName: preset.packageName }),
      description: t(
        target.breaking ? "CONNECTIONS_CARD.UPDATED_BREAKING" : "CONNECTIONS_CARD.UPDATED_SAVE",
        { from: target.from, to: target.to },
      ),
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
      showErrorToast({
        title: t("CONNECTIONS_CARD.COULD_NOT_ADD", { label: preset.label }),
        errors: [result.reason],
      });
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
      result.filledKeys.length > 0
        ? t("CONNECTIONS_CARD.FILLED_IN", { value: result.filledKeys.join(", ") })
        : null,
      secrets.length > 0
        ? t("CONNECTIONS_CARD.BIND_TO_A_CONFIGURATION", {
            value: secrets.map((variable) => variable.key).join(", "),
          })
        : null,
    ];
    showSuccessToast({
      title: t("CONNECTIONS_CARD.SET_UP", { label: preset.label }),
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
            {t("CONNECTIONS_CARD.CONNECTIONS")}
          </span>
          <span className="text-xs leading-relaxed text-medium-emphasis">
            {t("CONNECTIONS_CARD.ADDS_THE_PACKAGE_AND")}
          </span>
        </div>

        {hasIssues && (
          <output
            aria-label={t("CONNECTIONS_CARD.SETUP_CHECK")}
            className="flex flex-col gap-2 rounded-md border border-warning-200 bg-warning-50 p-2.5 text-xs"
          >
            {!setup.manifestValid && (
              <SetupIssue>
                {renderRichText(t("CONNECTIONS_CARD.N_0_PACKAGE_JSON_0"), [
                  (children) => <code className="font-mono">{children}</code>,
                ])}
              </SetupIssue>
            )}
            {setup.missingPackages.map((name) => {
              const preset = presetForPackage(name);
              return (
                <SetupIssue key={name}>
                  <span className="min-w-0 [overflow-wrap:anywhere]">
                    {renderRichText(t("CONNECTIONS_CARD.N_0_0_IS_IMPORTED", { name }), [
                      (children) => <code className="font-mono">{children}</code>,
                    ])}
                  </span>
                  {preset ? (
                    <button
                      type="button"
                      className="font-semibold text-primary hover:underline"
                      onClick={() => void add(preset)}
                      disabled={pendingId !== null}
                    >
                      {pendingId === preset.id
                        ? t("CONNECTIONS_CARD.CHECKING_NPM")
                        : t("CONNECTIONS_CARD.ADD", { name })}
                    </button>
                  ) : (
                    <span className="text-medium-emphasis">
                      {t("CONNECTIONS_CARD.ADD_IT_WITH_AN")}
                    </span>
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
                  {t("CONNECTIONS_CARD.READ_BUT_NOT_DEFINED", {
                    count: setup.missingVariables.length,
                  })}
                </span>
                <button
                  type="button"
                  className="font-semibold text-primary hover:underline"
                  onClick={() =>
                    onVariablesChange(addMissingVariables(variables, setup.missingVariables))
                  }
                >
                  {t("CONNECTIONS_CARD.ADD_VARIABLES", { count: setup.missingVariables.length })}
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
                  {t("CONNECTIONS_CARD.NO_VALUE", { count: setup.emptyVariables.length })}
                </span>
                {fillable.filledKeys.length > 0 && (
                  <button
                    type="button"
                    className="font-semibold text-primary hover:underline"
                    onClick={() => onVariablesChange(fillable.variables)}
                  >
                    {t("CONNECTIONS_CARD.FILL_IN", { value: fillable.filledKeys.join(", ") })}
                  </button>
                )}
                <button
                  type="button"
                  className="font-semibold text-primary hover:underline"
                  onClick={onEditVariables}
                >
                  {t("CONNECTIONS_CARD.EDIT_VARIABLES")}
                </button>
              </SetupIssue>
            )}
          </output>
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
                aria-label={t("CONNECTIONS_CARD.PRESET_STATUS", {
                  label: preset.label,
                  status: available
                    ? t("CONNECTIONS_CARD.UPDATE_AVAILABLE", { to: available.to })
                    : statusLabel(status),
                })}
              >
                <span className="flex min-w-0 flex-col">
                  <span className="text-xs font-semibold">{preset.label}</span>
                  <code className="truncate font-mono text-[11px] text-low-emphasis">
                    {preset.packageName}
                  </code>
                </span>
                <PresetStatusBadge status={status} available={available} />
              </button>
            );
          })}
        </div>

        <p className="text-xs leading-relaxed text-low-emphasis">
          {t("CONNECTIONS_CARD.ONLY_PUBLIC_ENDPOINTS_ARE")}
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

const statusLabel = (status: "added" | "partial" | "none") => {
  if (status === "added")
    return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "CONNECTIONS_CARD.STATUS_ADDED");
  if (status === "partial")
    return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "CONNECTIONS_CARD.STATUS_INCOMPLETE");
  return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "CONNECTIONS_CARD.STATUS_NOT_SET_UP");
};

const PresetStatusBadge = ({
  status,
  available,
}: {
  status: "added" | "partial" | "none";
  available: { from: string; to: string } | null;
}) => {
  const { t } = useT(LOGIC_FUNCTIONS_COMPONENTS_MODULE);
  if (available) {
    return (
      <Badge variant="info" className="gap-1" title={`${available.from} → ${available.to}`}>
        <ArrowUpCircle className="h-3 w-3" />
        {t("common:UPDATE")}
      </Badge>
    );
  }
  if (status === "added") {
    return (
      <Badge variant="success" className="gap-1">
        <Check className="h-3 w-3" />
        {t("CONNECTIONS_CARD.ADDED")}
      </Badge>
    );
  }
  if (status === "partial") {
    return <Badge variant="warning">{t("CONNECTIONS_CARD.INCOMPLETE")}</Badge>;
  }
  return <span className="text-xs font-semibold text-primary">{t("CONNECTIONS_CARD.SET_UP_2")}</span>;
};

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
  const { t } = useT(LOGIC_FUNCTIONS_COMPONENTS_MODULE);
  const [copied, setCopied] = useState(false);
  const status = presetStatus(preset, dependencies, variables);
  const keys = new Set(variables.map((variable) => variable.key));
  /** The value adding would give this row: only when it is missing or blank, never over a typed one. */
  const fillsTo = (variable: IConnectionPreset["variables"][number]) =>
    variables.some((row) => row.key === variable.key && row.value.trim() !== "")
      ? ""
      : fillKnownValues([{ key: variable.key, value: "" }], { blocksApiHost }).variables[0].value;
  const listed = dependencies[preset.packageName];
  const listedNote =
    listed === undefined
      ? t("CONNECTIONS_CARD.TODAY_S_LATEST_PINNED")
      : t("CONNECTIONS_CARD.IN_PACKAGE_JSON_LEFT");
  let addLabel = t("CONNECTIONS_CARD.ADD_TO_FUNCTION");
  if (status === "added") addLabel = t("CONNECTIONS_CARD.ALREADY_SET_UP");
  else if (isAdding) addLabel = t("CONNECTIONS_CARD.CHECKING_NPM");

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(preset.snippet);
      setCopied(true);
      setTimeout(() => setCopied(false), 1400);
    } catch {
      showErrorToast({
        title: t("CONNECTIONS_CARD.COULD_NOT_COPY"),
        errors: [t("CONNECTIONS_CARD.SELECT_THE_EXAMPLE_AND")],
      });
    }
  };

  return (
    <DialogContent className="max-h-[90vh] w-[calc(100%-2rem)] max-w-2xl overflow-y-auto">
      <DialogHeader className="pr-6">
        <DialogTitle>{preset.label}</DialogTitle>
        <DialogDescription>{preset.description}</DialogDescription>
      </DialogHeader>

      <div className="flex flex-col gap-1.5">
        <span className="text-xs font-semibold uppercase tracking-wide text-low-emphasis">
          {t("CONNECTIONS_CARD.PACKAGE")}
        </span>
        <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2 rounded-md border px-3 py-2">
          <code className="min-w-0 font-mono text-xs [overflow-wrap:anywhere]">
            {preset.packageName}@{listed ?? "latest"}
          </code>
          {update ? (
            <span className="flex flex-wrap items-center gap-2">
              <span className="text-xs text-medium-emphasis">
                {t("CONNECTIONS_CARD.IS_THE_LATEST", { to: update.to })}
              </span>
              <Button type="button" size="sm" variant="outline" onClick={onUpdate}>
                {t("CONNECTIONS_CARD.UPDATE_TO", { to: update.to })}
              </Button>
            </span>
          ) : (
            <span className="text-xs text-medium-emphasis">{listedNote}</span>
          )}
        </div>
        {update?.breaking && (
          <span className="flex items-start gap-1.5 text-xs text-warning-800">
            <TriangleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            {t("CONNECTIONS_CARD.IS_A_BREAKING_RELEASE", { to: update.to })}
          </span>
        )}
      </div>

      <div className="flex flex-col gap-1.5">
        <span className="text-xs font-semibold uppercase tracking-wide text-low-emphasis">
          {t("CONNECTIONS_CARD.VARIABLES")}
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
                    {t("CONNECTIONS_CARD.BIND_TO_A_SECRET")}
                  </Badge>
                )}
                {keys.has(variable.key) && (
                  <span className="text-xs text-medium-emphasis">
                    {t("CONNECTIONS_CARD.ALREADY_DEFINED")}
                  </span>
                )}
              </span>
              <span className="break-all text-xs text-medium-emphasis">{variable.hint}</span>
              {fillsTo(variable) && (
                <span className="break-all text-xs">
                  {renderRichText(
                    t("CONNECTIONS_CARD.WILL_BE_SET_TO", { value: fillsTo(variable) }),
                    [(children) => <code className="font-mono">{children}</code>],
                  )}
                </span>
              )}
            </div>
          ))}
        </div>
      </div>

      <div className="flex flex-col gap-1.5">
        <div className="flex items-center justify-between">
          <span className="text-xs font-semibold uppercase tracking-wide text-low-emphasis">
            {t("CONNECTIONS_CARD.EXAMPLE")}
          </span>
          <button
            type="button"
            className="text-xs font-semibold text-primary hover:underline"
            onClick={copy}
          >
            {copied ? t("CONNECTIONS_CARD.COPIED") : t("common:COPY")}
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
          {addLabel}
        </Button>
      </DialogFooter>
    </DialogContent>
  );
};
