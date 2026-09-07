import { Badge } from "@/components/ui-kits/badge/badge";
import { Proxy } from "../types";

export const ProxyStatusBadge = ({ proxy }: { proxy: Pick<Proxy, "enabled"> }) => (
  <Badge
    variant={proxy.enabled ? "success" : "error"}
    className="rounded-md px-2.5 py-0.5 text-xs font-semibold"
  >
    {proxy.enabled ? "Live" : "Paused"}
  </Badge>
);

