import { Badge } from "@/components/ui-kits/badge/badge";
import { FunctionStatus } from "../../types/function.types";
import { FUNCTION_STATUS_LABELS } from "../../constants/limits.constant";

const VARIANT_BY_STATUS: Record<FunctionStatus, "success" | "secondary" | "info"> = {
  Live: "success",
  Draft: "secondary",
  Paused: "info",
};

export const FunctionStatusChip = ({ status }: { status: FunctionStatus }) => (
  <Badge variant={VARIANT_BY_STATUS[status] ?? "secondary"} className="w-fit rounded-md px-2.5 py-0.5">
    {FUNCTION_STATUS_LABELS[status] ?? status}
  </Badge>
);
