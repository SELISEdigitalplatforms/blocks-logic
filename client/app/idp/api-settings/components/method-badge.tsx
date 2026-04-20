import { cn } from "@/lib/utils";

const METHOD_STYLES: Record<string, string> = {
  GET: "bg-emerald-500/15 text-emerald-600 dark:text-emerald-400",
  POST: "bg-blue-500/15 text-blue-600 dark:text-blue-400",
  PUT: "bg-amber-500/15 text-amber-600 dark:text-amber-400",
  PATCH: "bg-purple-500/15 text-purple-600 dark:text-purple-400",
  DELETE: "bg-red-500/15 text-red-600 dark:text-red-400",
};

const FALLBACK_STYLE = "bg-muted text-muted-foreground";

type MethodBadgeProps = {
  method: string;
};

export const MethodBadge = ({ method }: MethodBadgeProps) => {
  const upper = method.toUpperCase();
  return (
    <span
      className={cn(
        "inline-flex w-[70px] items-center justify-center rounded px-2 py-0.5 text-xs font-bold uppercase tracking-wide",
        METHOD_STYLES[upper] || FALLBACK_STYLE,
      )}
    >
      {upper}
    </span>
  );
};
