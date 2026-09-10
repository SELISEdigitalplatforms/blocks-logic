"use client";
import { useState } from "react";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Button } from "@/components/ui-kits/button/button";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import { cn } from "@/lib/utils";
import { IFunctionVersionSummary } from "../../types/version.types";
import {
  formatAbsoluteTime,
  formatRelativeTime,
  formatRunCount,
} from "../../utils/format";
import { RollbackDialog } from "../rollback-dialog";
import { VersionSourceDialog } from "../version-source-dialog";

/** Design's grid: Version · Deployed · Packages · Runs · action. */
const GRID =
  "grid grid-cols-[74px_minmax(0,1fr)_minmax(0,0.8fr)_minmax(0,auto)_150px] items-center gap-3";

type VersionsTableProps = {
  functionId: string;
  versions: IFunctionVersionSummary[];
  activeVersionNumber?: number | null;
  isLoading: boolean;
};

export const VersionsTable = ({
  functionId,
  versions,
  activeVersionNumber,
  isLoading,
}: VersionsTableProps) => {
  const [rollbackTarget, setRollbackTarget] = useState<IFunctionVersionSummary | null>(null);
  const [sourceTarget, setSourceTarget] = useState<IFunctionVersionSummary | null>(null);

  return (
    <div className="flex max-w-[900px] flex-col gap-3">
      <p className="max-w-[68ch] text-xs leading-relaxed text-medium-emphasis">
        Every deploy builds an immutable image. Rolling back re-points the active version — no
        rebuild, and in-flight runs finish on the version they started with.
      </p>

      <div className="overflow-hidden rounded-lg border">
        <div
          className={`${GRID} border-b bg-surface-app px-4 py-2.5 text-xs font-medium uppercase tracking-wide text-low-emphasis`}
        >
          <span>Version</span>
          <span>Deployed</span>
          <span>Packages</span>
          <span>Runs</span>
          <span />
        </div>

        {isLoading && (
          <div className="flex flex-col gap-2 p-3">
            {Array.from({ length: 4 }).map((_, index) => (
              <Skeleton key={index} className="h-10 w-full" />
            ))}
          </div>
        )}

        {!isLoading && versions.length === 0 && (
          <p className="px-5 py-9 text-center text-sm text-medium-emphasis">
            No versions yet — the first deploy creates v1.
          </p>
        )}

        {!isLoading &&
          versions.map((version) => {
            const isActive = version.number === activeVersionNumber;
            return (
              <div
                key={version.id}
                className={cn(
                  `${GRID} border-b px-4 py-3 last:border-b-0`,
                  isActive && "bg-blocks-primary-25",
                )}
              >
                <code
                  className={cn(
                    "font-mono text-xs font-semibold",
                    isActive ? "text-primary" : "text-foreground",
                  )}
                >
                  v{version.number}
                </code>

                <div className="flex min-w-0 flex-col gap-0.5">
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <span className="text-xs">{formatRelativeTime(version.createdDate)}</span>
                    </TooltipTrigger>
                    <TooltipContent>{formatAbsoluteTime(version.createdDate)}</TooltipContent>
                  </Tooltip>
                  <span className="truncate text-xs text-low-emphasis">
                    {version.createdBy || "—"}
                    {version.note ? ` · ${version.note}` : ""}
                  </span>
                </div>

                <span className="min-w-0 break-words font-mono text-xs text-medium-emphasis">
                  {version.packages || "no dependencies"}
                </span>

                <span className="whitespace-nowrap text-xs text-medium-emphasis">
                  {formatRunCount(version.runCount)}
                </span>

                <div className="flex items-center justify-end gap-2">
                  <Button
                    variant="ghost"
                    size="xs"
                    className="px-2 text-xs"
                    onClick={() => setSourceTarget(version)}
                  >
                    View source
                  </Button>
                  {isActive ? (
                    <span className="rounded bg-success/15 px-2 py-0.5 text-[10px] font-semibold uppercase text-success">
                      Active
                    </span>
                  ) : (
                    <Button
                      variant="outline"
                      size="xs"
                      className="text-xs"
                      onClick={() => setRollbackTarget(version)}
                    >
                      Roll back
                    </Button>
                  )}
                </div>
              </div>
            );
          })}
      </div>

      <RollbackDialog
        functionId={functionId}
        version={rollbackTarget}
        open={!!rollbackTarget}
        onOpenChange={(open) => !open && setRollbackTarget(null)}
      />
      <VersionSourceDialog
        functionId={functionId}
        version={sourceTarget}
        open={!!sourceTarget}
        onOpenChange={(open) => !open && setSourceTarget(null)}
      />
    </div>
  );
};
