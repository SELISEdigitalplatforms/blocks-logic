import { Badge } from "@/components/ui-kits/badge/badge";
import { RunStatus } from "../../types/run.types";
import { RUN_STATUS_LABELS } from "../../constants/limits.constant";

const VARIANT_BY_STATUS: Record<RunStatus, "success" | "error" | "info" | "secondary"> = {
  Succeeded: "success",
  Failed: "error",
  TimedOut: "error",
  ResourceExceeded: "error",
  OutputFailed: "error",
  Cancelled: "secondary",
  Queued: "secondary",
  Claimed: "info",
  Starting: "info",
  Running: "info",
  OutputProcessing: "info",
};

export const RunStatusChip = ({ status }: { status: RunStatus }) => (
  <Badge variant={VARIANT_BY_STATUS[status] ?? "secondary"} className="w-fit rounded-md px-2.5 py-0.5">
    {RUN_STATUS_LABELS[status] ?? status}
  </Badge>
);
