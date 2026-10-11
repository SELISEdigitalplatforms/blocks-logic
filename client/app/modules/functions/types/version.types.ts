export interface IFunctionVersionSummary {
  id: string;
  number: number;
  imageDigest: string;
  note?: string | null;
  /** Resolved dependencies, as the builder reported them. */
  packages?: string | null;
  /** Runs recorded against this version. */
  runCount: number;
  createdDate: string;
  createdBy: string;
}

export interface IGetVersionsResponse {
  data: IFunctionVersionSummary[] | null;
  totalCount: number;
}

export type BuildStatus = "Queued" | "Building" | "Succeeded" | "Failed";

/** Deploy's 202 answer: the build outlasted the request's short wait. Watch it, then deploy it. */
export interface IDeployPending {
  buildId: string;
  status: BuildStatus;
}

export const isDeployPending = (result: unknown): result is IDeployPending =>
  typeof result === "object" && result !== null && "buildId" in result && !("number" in result);

export interface IFunctionBuild {
  id: string;
  status: BuildStatus;
  imageDigest?: string | null;
  packages?: string | null;
  log?: string | null;
  errorMessage?: string | null;
  createdDate: string;
  completedAt?: string | null;
}
