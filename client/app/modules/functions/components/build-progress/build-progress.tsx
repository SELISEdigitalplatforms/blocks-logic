import { useState } from "react";
import { Loader2, CheckCircle2, XCircle, ChevronDown, ChevronRight } from "lucide-react";
import { useGetBuild } from "../../hooks/use-versions";
import { cn } from "@/lib/utils";

const LABELS: Record<string, string> = {
  Queued: "Queued for build…",
  Building: "Building image…",
  Succeeded: "Build succeeded",
  Failed: "Build failed",
};

/**
 * Build state, and — on demand — the builder's own output.
 *
 * The log is where a build failure is actually explained: `npm error 404 Not Found - GET
 * https://registry.npmjs.org/kyy` says what the one-line message never can. It used to be
 * fetched and thrown away, so the only way to read it was the network tab.
 */
export const BuildProgress = ({ buildId }: { buildId?: string }) => {
  const { data: build } = useGetBuild(buildId);
  // null means "nobody has clicked yet", which is what lets a failed build open itself without
  // taking the toggle away: `false` from a click has to beat the default, not be equal to it.
  const [isLogOpen, setIsLogOpen] = useState<boolean | null>(null);

  if (!build) return null;

  const hasLog = !!build.log?.trim();
  const showLog = (isLogOpen ?? build.status === "Failed") && hasLog;

  return (
    <div className="flex flex-col gap-2 rounded-lg border bg-muted/20 px-3 py-2 text-sm">
      <div className="flex flex-wrap items-center gap-2">
        {(build.status === "Queued" || build.status === "Building") && (
          <Loader2 className="h-4 w-4 animate-spin text-primary" />
        )}
        {build.status === "Succeeded" && <CheckCircle2 className="h-4 w-4 text-green-500" />}
        {build.status === "Failed" && <XCircle className="h-4 w-4 text-error" />}
        <span>{LABELS[build.status] ?? build.status}</span>

        {hasLog && (
          <button
            type="button"
            aria-expanded={showLog}
            className="ml-auto flex items-center gap-1 rounded-md px-1.5 py-0.5 text-xs font-medium text-medium-emphasis hover:bg-surface-app"
            onClick={() => setIsLogOpen(!showLog)}
          >
            {showLog ? (
              <ChevronDown className="h-3.5 w-3.5" />
            ) : (
              <ChevronRight className="h-3.5 w-3.5" />
            )}
            {showLog ? "Hide build log" : "Build log"}
          </button>
        )}
      </div>

      {/* Not truncated: the message carries the reason, and clipping it at the panel width is how
          a build failure turns into a trip to the network tab. */}
      {build.status === "Failed" && build.errorMessage && (
        <pre className="whitespace-pre-wrap break-words font-mono text-xs leading-relaxed text-error">
          {build.errorMessage}
        </pre>
      )}

      {showLog && (
        <pre
          className={cn(
            "max-h-64 overflow-auto rounded-md border bg-surface-app p-2.5",
            "whitespace-pre-wrap break-words font-mono text-[11px] leading-relaxed",
          )}
        >
          {build.log}
        </pre>
      )}

      {/* What the build actually resolved, as recorded on the build — with no lockfile, this is
          the only statement of which versions the image contains. */}
      {build.status === "Succeeded" && build.packages && build.packages !== "[]" && (
        <p className="break-words font-mono text-[11px] leading-relaxed text-low-emphasis">
          Installed {build.packages}
        </p>
      )}
    </div>
  );
};
