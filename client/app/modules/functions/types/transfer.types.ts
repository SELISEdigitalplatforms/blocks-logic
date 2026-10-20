import type {
  IOutputAction,
  ITriggerConfig,
  IVariableBinding,
} from "./function.types";

/**
 * One function as an export file (`blocks.function.export`, schema 2) — the server's
 * `FunctionExportFile`. The same `itemId` in every environment; secrets appear by name only.
 */
export interface IFunctionExportFile {
  format: string;
  schemaVersion: number;
  exportedAt: string;
  function: {
    itemId: string;
    name: string;
    description?: string | null;
    source: {
      indexJs: string;
      packageJson: string;
      lockJson?: string | null;
      allowInstallScripts: boolean;
    };
    /** The function's own HTTP rate limit; absent when unset. Limits and retry are fixed by the platform, never exported. */
    requestsPerMinute?: number | null;
    trigger: ITriggerConfig;
    outputActions: IOutputAction[];
    variables: IVariableBinding[];
  };
  secretNames: { name: string; usedBy: string[] }[];
  unresolvedSecretRefs?: string[];
  contentHash: string;
}

export type FunctionImportStatus = "new" | "same" | "changed" | "blocked";

/** A setting or file an import can take or keep. Same names as the server's `Fields`. */
export type FunctionImportField =
  | "name"
  | "description"
  | "trigger"
  | "rateLimit"
  | "variables"
  | "outputActions"
  | "indexJs"
  | "packageJson"
  | "lockJson";

export type FunctionImportChoice = "mine" | "incoming";

export interface IFunctionImportFieldDiff {
  field: FunctionImportField;
  label: string;
  mine?: string | null;
  incoming?: string | null;
}

export interface IFunctionImportFileDiff {
  field: FunctionImportField;
  file: string;
  language: "javascript" | "json";
  mine?: string | null;
  incoming?: string | null;
}

export interface IFunctionImportPreview {
  status: FunctionImportStatus;
  functionId: string;
  name: string;
  contentHash: string;
  targetName?: string | null;
  /** Send back as `expectedLastUpdatedDate`; the server answers 409 when it moved. */
  targetLastUpdatedDate?: string | null;
  blockedReason?: string | null;
  fields: IFunctionImportFieldDiff[];
  files: IFunctionImportFileDiff[];
  missingSecrets: string[];
  duplicateSecrets: string[];
  unresolvedSecretRefs: string[];
  secretsUnchecked: boolean;
  allowInstallScriptsRequested: boolean;
  warnings: string[];
}

export type FunctionImportResolution = "takeIncoming" | "keepMine" | "merge";

export interface IFunctionImportApplyPayload {
  file: IFunctionExportFile;
  resolution?: FunctionImportResolution;
  fieldChoices?: Partial<Record<FunctionImportField, FunctionImportChoice>>;
  expectedLastUpdatedDate?: string | null;
}

export type FunctionImportDeployState = "live" | "building" | "failed";

export interface IFunctionImportResult {
  status: "created" | "updated" | "unchanged";
  functionId: string;
  name: string;
  allowInstallScriptsReset: boolean;
  missingSecrets: string[];
  duplicateSecrets: string[];
  messages: string[];
  deploy?: {
    state: FunctionImportDeployState;
    buildId?: string | null;
    versionNumber?: number | null;
    error?: string | null;
  } | null;
}
