import { Badge } from "@/components/ui-kits/badge/badge";
import { Proxy } from "../types";

export const ProxyStatusBadge = ({ proxy }: { proxy: Pick<Proxy, "enabled"> }) => (
  <Badge
    variant={proxy.enabled ? "success" : "secondary"}
    className={
      proxy.enabled
        ? "rounded-md px-2.5 py-0.5 text-xs font-semibold"
        : "rounded-md border-slate-300 bg-slate-200 px-2.5 py-0.5 text-xs font-semibold text-slate-800 hover:bg-slate-200 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-800"
    }
  >
    {proxy.enabled ? "Live" : "Paused"}
  </Badge>
);
