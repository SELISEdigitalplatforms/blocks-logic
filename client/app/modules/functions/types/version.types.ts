export interface IFunctionVersionSummary {
  id: string;
  number: number;
  imageDigest: string;
  note?: string | null;
  createdDate: string;
  createdBy: string;
}

export interface IGetVersionsResponse {
  data: IFunctionVersionSummary[] | null;
  totalCount: number;
}

export type BuildStatus = "Queued" | "Building" | "Succeeded" | "Failed";

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
