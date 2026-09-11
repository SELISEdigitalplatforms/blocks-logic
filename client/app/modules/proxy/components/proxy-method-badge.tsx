import { Badge } from "@/components/ui-kits/badge/badge";
import { cn } from "@/lib/utils";
import { ProxyMethod } from "../types";

const methodClass: Record<ProxyMethod, string> = {
  GET: "border-sky-200 bg-sky-50 text-sky-700 dark:border-sky-900 dark:bg-sky-950 dark:text-sky-300",
  POST: "border-emerald-200 bg-emerald-50 text-emerald-700 dark:border-emerald-900 dark:bg-emerald-950 dark:text-emerald-300",
  PUT: "border-amber-200 bg-amber-50 text-amber-700 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-300",
  PATCH:
    "border-violet-200 bg-violet-50 text-violet-700 dark:border-violet-900 dark:bg-violet-950 dark:text-violet-300",
  DELETE: "border-red-200 bg-red-50 text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300",
};

type Props = {
  method: ProxyMethod;
  className?: string;
};

export const ProxyMethodBadge = ({ method, className }: Props) => (
  <Badge
    variant="outline"
    className={cn(
      "w-fit border font-mono text-[11px] font-bold leading-none tracking-normal",
      methodClass[method],
      className,
    )}
  >
    {method}
  </Badge>
);
