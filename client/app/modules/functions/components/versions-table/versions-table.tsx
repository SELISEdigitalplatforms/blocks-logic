"use client";
import { useState } from "react";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui-kits/table/table";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { formatDate, parseDateString } from "@/lib/utils";
import { IFunctionVersionSummary } from "../../types/version.types";
import { RollbackDialog } from "../rollback-dialog";

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

  return (
    <>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Version</TableHead>
            <TableHead>Note</TableHead>
            <TableHead>Created</TableHead>
            <TableHead>By</TableHead>
            <TableHead></TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {!isLoading && versions.length === 0 && (
            <TableRow>
              <TableCell colSpan={5} className="py-8 text-center text-sm text-muted-foreground">
                No versions deployed yet.
              </TableCell>
            </TableRow>
          )}
          {versions.map((version) => (
            <TableRow key={version.id}>
              <TableCell className="font-semibold">
                v{version.number}
                {version.number === activeVersionNumber && (
                  <Badge variant="success" className="ml-2 rounded-md px-2 py-0.5">
                    Active
                  </Badge>
                )}
              </TableCell>
              <TableCell className="text-muted-foreground">{version.note || "-"}</TableCell>
              <TableCell className="whitespace-nowrap text-muted-foreground">
                {formatDate(parseDateString(version.createdDate))}
              </TableCell>
              <TableCell className="text-muted-foreground">{version.createdBy || "-"}</TableCell>
              <TableCell>
                {version.number !== activeVersionNumber && (
                  <Button variant="outline" size="sm" onClick={() => setRollbackTarget(version)}>
                    Roll back
                  </Button>
                )}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      <RollbackDialog
        functionId={functionId}
        version={rollbackTarget}
        open={!!rollbackTarget}
        onOpenChange={(open) => !open && setRollbackTarget(null)}
      />
    </>
  );
};
