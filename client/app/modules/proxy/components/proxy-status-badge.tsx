import { Badge } from "@/components/ui-kits/badge/badge";
import { Proxy } from "../types";

export const ProxyStatusBadge = ({ proxy }: { proxy: Pick<Proxy, "enabled"> }) => (
  <Badge
    variant={proxy.enabled ? "success" : "secondary"}
    className={
      proxy.enabled
        ? "rounded-md px-2.5 py-0.5 text-xs font-semibold"
        : "rounded-md border-transparent bg-amber-100 px-2.5 py-0.5 text-xs font-semibold text-amber-800"
    }
  >
    {proxy.enabled ? "Live" : "Paused"}
  </Badge>
);
