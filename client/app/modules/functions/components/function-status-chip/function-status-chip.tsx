import { Badge } from "@/components/ui-kits/badge/badge";
import { FunctionStatus } from "../../types/function.types";
import { FUNCTION_STATUS_LABELS } from "../../constants/limits.constant";

/**
 * FEATURES-AND-UI §4.3 pairs DRAFT with amber and LIVE with green. `Paused` is a server status
 * the design never drew (it predates the spec's BUILDING chip, which no function status maps to);
 * it takes neutral rather than the spec's blue, so a disabled function does not read as "building".
 */
const VARIANT_BY_STATUS: Record<FunctionStatus, "success" | "secondary" | "warning"> = {
  Live: "success",
  Draft: "warning",
  Paused: "secondary",
};

export const FunctionStatusChip = ({ status }: { status: FunctionStatus }) => (
  <Badge
    variant={VARIANT_BY_STATUS[status] ?? "secondary"}
    className="w-fit rounded-md px-2.5 py-0.5"
  >
    {FUNCTION_STATUS_LABELS[status] ?? status}
  </Badge>
);
