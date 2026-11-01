"use client";
import { useState } from "react";
import { FileCode2 } from "lucide-react";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Button } from "@/components/ui-kits/button/button";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { cn } from "@/lib/utils";
import { IFunctionVersionSummary } from "../../types/version.types";
import { formatAbsoluteTime, formatRelativeTime, formatRunCount } from "../../utils/format";
import { VersionSourceDialog } from "../version-source-dialog";
import { useT } from "@seliseblocks/genesis-os";

// Keep header, loading placeholders, and rows on identical column tracks.
const GRID =
  "grid min-w-[720px] grid-cols-[64px_minmax(0,1fr)_minmax(0,1fr)_64px_86px_48px] items-center gap-3";

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
  const { t } = useT("logic.functions.components");
  const rowActionsLabel = t("VERSIONS_TABLE.ROW_ACTIONS");
  const [sourceTarget, setSourceTarget] = useState<IFunctionVersionSummary | null>(null);

  return (
    <div className="flex flex-col gap-3">
      <p className="max-w-[68ch] text-xs leading-relaxed text-medium-emphasis">
        {t("VERSIONS_TABLE.EVERY_DEPLOY_PINS_AN")}
      </p>

      <div /* NOSONAR: CSS-grid table */
        className="overflow-x-auto rounded-lg border"
        role="table"
        aria-label={t("VERSIONS_TABLE.VERSIONS")}
      >
        <div
          className={`${GRID} border-b bg-surface-app px-4 py-2.5 text-xs font-medium uppercase tracking-wide text-low-emphasis`}
          role="row"
        >
          <span /* NOSONAR: CSS-grid table */ role="columnheader">
            {t("VERSIONS_TABLE.VERSION")}
          </span>
          <span /* NOSONAR: CSS-grid table */ role="columnheader">
            {t("VERSIONS_TABLE.DEPLOYED")}
          </span>
          <span /* NOSONAR: CSS-grid table */ role="columnheader">{t("VERSIONS_TABLE.NOTE")}</span>
          <span /* NOSONAR: CSS-grid table */ role="columnheader">{t("VERSIONS_TABLE.RUNS")}</span>
          <span /* NOSONAR: CSS-grid table */ role="columnheader">{t("common:STATUS")}</span>
          <span /* NOSONAR: CSS-grid table */ role="columnheader" aria-label={rowActionsLabel} />
        </div>

        {isLoading &&
          Array.from({ length: 4 }).map((_, index) => (
            // Same frame as a loaded row, so the card keeps its shape and height on load.
            <div key={index} className={`${GRID} border-b px-4 py-3 last:border-b-0`}>
              <Skeleton className="h-3.5 w-8" />
              <div className="flex flex-col gap-1">
                <Skeleton className="h-3 w-24" />
                <Skeleton className="h-3 w-32" />
              </div>
              <Skeleton className="h-3 w-full max-w-40" />
              <Skeleton className="h-3 w-8" />
              <Skeleton className="h-5 w-16" />
              <Skeleton className="h-8 w-8 justify-self-end" />
            </div>
          ))}

        {!isLoading && versions.length === 0 && (
          <p className="px-5 py-9 text-center text-sm text-medium-emphasis">
            {t("VERSIONS_TABLE.NO_VERSIONS_YET_THE")}
          </p>
        )}

        {!isLoading &&
          versions.map((version) => {
            const isActive = version.number === activeVersionNumber;
            return (
              <div
                key={version.id}
                role="row"
                className={cn(
                  `${GRID} border-b px-4 py-3 last:border-b-0`,
                  isActive && "bg-blocks-primary-25",
                )}
              >
                <code
                  role="cell"
                  className={cn(
                    "font-mono text-xs font-semibold",
                    isActive ? "text-primary" : "text-foreground",
                  )}
                >
                  v{version.number}
                </code>

                <div className="flex min-w-0 flex-col gap-0.5" role="cell">
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <span className="text-xs">{formatRelativeTime(version.createdDate)}</span>
                    </TooltipTrigger>
                    <TooltipContent>{formatAbsoluteTime(version.createdDate)}</TooltipContent>
                  </Tooltip>
                  <span className="truncate text-xs text-low-emphasis" title={version.createdBy}>
                    {version.createdBy || "—"}
                  </span>
                </div>

                <span className="min-w-0 break-words text-xs text-medium-emphasis" role="cell">
                  {version.note || "—"}
                </span>

                <span className="whitespace-nowrap text-xs text-medium-emphasis" role="cell">
                  {formatRunCount(version.runCount)}
                </span>

                <div role="cell">
                  {isActive ? (
                    <span className="rounded bg-success/15 px-2 py-0.5 text-[10px] font-semibold uppercase text-success">
                      {t("VERSIONS_TABLE.ACTIVE")}
                    </span>
                  ) : (
                    <span className="text-xs text-low-emphasis">
                      {activeVersionNumber == null ? "—" : t("VERSIONS_TABLE.INACTIVE")}
                    </span>
                  )}
                </div>

                <div className="flex justify-end" role="cell">
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        className="h-8 w-8 text-medium-emphasis"
                        aria-label={t("VERSIONS_TABLE.VIEW_SOURCE_OF_V", {
                          number: version.number,
                        })}
                        onClick={() => setSourceTarget(version)}
                      >
                        <FileCode2 className="h-4 w-4" aria-hidden="true" />
                      </Button>
                    </TooltipTrigger>
                    <TooltipContent>{t("VERSIONS_TABLE.VIEW_SOURCE")}</TooltipContent>
                  </Tooltip>
                </div>
              </div>
            );
          })}
      </div>

      <VersionSourceDialog
        functionId={functionId}
        version={sourceTarget}
        open={!!sourceTarget}
        onOpenChange={(open) => !open && setSourceTarget(null)}
      />
    </div>
  );
};
