import { Loader2, CheckCircle2, XCircle } from "lucide-react";
import { useGetBuild } from "../../hooks/use-versions";

const LABELS: Record<string, string> = {
  Queued: "Queued for build…",
  Building: "Building image…",
  Succeeded: "Build succeeded",
  Failed: "Build failed",
};

export const BuildProgress = ({ buildId }: { buildId?: string }) => {
  const { data: build } = useGetBuild(buildId);
  if (!build) return null;

  return (
    <div className="flex items-center gap-2 rounded-lg border bg-muted/20 px-3 py-2 text-sm">
      {(build.status === "Queued" || build.status === "Building") && (
        <Loader2 className="h-4 w-4 animate-spin text-primary" />
      )}
      {build.status === "Succeeded" && <CheckCircle2 className="h-4 w-4 text-green-500" />}
      {build.status === "Failed" && <XCircle className="h-4 w-4 text-error" />}
      <span>{LABELS[build.status] ?? build.status}</span>
      {build.status === "Failed" && build.errorMessage && (
        <span className="truncate text-xs text-muted-foreground">— {build.errorMessage}</span>
      )}
    </div>
  );
};
