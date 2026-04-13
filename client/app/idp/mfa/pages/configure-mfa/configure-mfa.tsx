

import { useMemo, useState } from "react";
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { Button } from "@/components/ui-kits/button/button";
import { EllipsisVertical } from "lucide-react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Badge } from "@/components/ui-kits/badge/badge";
import { useGetMFAConfig, useSaveMFAConfig } from "../../hooks/use-mfa-config";
import { useProjectStore } from "@/store/useProjectStore";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { MFA_Provider_Data } from "../../utils/mfa-config";
import { LogMenu } from "@blocks-lmt/components";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Link } from "react-router-dom";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";

type MethodInfo = {
  enable: boolean;
  name: string;
  type: number | null;
};

const LoadingSkelton = () => {
  return (
    <div className="grid gap-2">
      {Array.from({ length: 3 }).map((_, index) => (
        <Skeleton key={index} className="h-12 w-full rounded" />
      ))}
    </div>
  );
};

export const ConfigureMFA = () => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { isLoading, isFetching, data } = useGetMFAConfig({ projectKey: tenantId });
  const [openEnableDisableModal, setOpenEnableDisableModal] = useState<boolean>(false);

  const [methodInfo, setMethodInfo] = useState<MethodInfo>({
    enable: false,
    name: "",
    type: null,
  });

  const columns: ColumnDef<(typeof MFA_Provider_Data)[number]>[] = useMemo(
    () => [
      {
        accessorKey: "provider",
        header: "Provider",
        cell: ({ row }) => (
          <div className="flex items-center gap-2">
            {<row.original.Icon className="h-4 w-4" />}
            <span>{row.original.label}</span>
          </div>
        ),
      },
      {
        accessorKey: "status",
        header: "Status",
        cell: ({ row }) => (
          <div className="flex max-w-[200px]">
            <Badge variant={data?.userMfaType.includes(row.original.type) ? "success" : "error"}>
              {data?.userMfaType.includes(row.original.type) ? "Enabled" : "Disabled"}
            </Badge>
          </div>
        ),
      },
      {
        id: "actions",
        cell: ({ row }) => (
          <div className="flex items-center justify-end">
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="sm" className="h-full p-1">
                  <EllipsisVertical className="h-5 w-5" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                {row.original.type === 2 && data?.userMfaType.includes(row.original.type) && (
                  <DropdownMenuItem>
                    <Link
                      to={`/utilities/email/communications/${data.mfaTemplate.templateId}/edit`}
                    >
                      Update Template
                    </Link>
                  </DropdownMenuItem>
                )}

                <DropdownMenuItem
                  onClick={async (e) => {
                    e.stopPropagation();
                    setOpenEnableDisableModal(true);
                    setMethodInfo(() => ({
                      enable: !data?.userMfaType.includes(row.original.type),
                      name: row.original.label,
                      type: row.original.type,
                    }));
                  }}
                >
                  {data?.userMfaType.includes(row.original.type) ? "Disable" : "Enable"}
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        ),
      },
    ],
    [data?.mfaTemplate.templateId, data?.userMfaType],
  );

  const { isPending, mutateAsync } = useSaveMFAConfig();

  const onSaveHandler = async (methodInfo: MethodInfo) => {
    const userMfaTypes = new Set(data?.userMfaType || []);
    const { type, enable } = methodInfo;

    if (!type) return;

    if (!enable) {
      if (userMfaTypes.has(type)) {
        userMfaTypes.delete(type);
      } else return;
    } else {
      userMfaTypes.add(type);
    }

    const payload = {
      projectKey: tenantId,
      enableMfa: !!userMfaTypes.size,
      userMfaType: Array.from(userMfaTypes),
    };

    const res = await mutateAsync(payload);
    if (!res.isSuccess) return showErrorToast({ errors: res.errors });
    showSuccessToast({
      description: `${methodInfo.name} MFA ${methodInfo.enable ? "enabled" : "disabled"} successfully`,
    });
    setOpenEnableDisableModal(false);
  };

  const mfaConfigData = useMemo(() => {
    if (!data) return [];
    return MFA_Provider_Data;
  }, [data]);

  const table = useReactTable({
    data: mfaConfigData,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  const loading = isLoading || isFetching;

  return (
    <>
      <div>
        <div className="mb-[18px] flex items-center justify-between md:mb-[24px]">
          <div className="item-center flex gap-2">
            <h1 className="text-lg font-semibold md:text-2xl">Multi-factor Authentication</h1>
          </div>
          <div className="flex items-center gap-2 md:gap-4">
            <Button
              size="sm"
              variant="outline"
              onClick={() =>
                window.open(
                  `${import.meta.env.BLOCKS_API_BASE_URL}/idp/v1/swagger/index.html`,
                  "_blank",
                )
              }
            >
              API Docs
            </Button>
            <LogMenu link="/services/mfa/logs" />
          </div>
        </div>

        <div className="flex w-full flex-col">
          <Card>
            <CardContent>
              {loading ? (
                <LoadingSkelton />
              ) : (
                <Table className="text-sm md:table-fixed">
                  <TableHeader>
                    {table.getHeaderGroups().map((headerGroup) => (
                      <TableRow key={headerGroup.id} className="hover:bg-transparent">
                        {headerGroup.headers.map((header) => (
                          <TableHead key={header.id}>
                            {flexRender(header.column.columnDef.header, header.getContext())}
                          </TableHead>
                        ))}
                      </TableRow>
                    ))}
                  </TableHeader>
                  <TableBody>
                    {table.getRowModel().rows.map((row) => (
                      <TableRow key={row.id}>
                        {row.getVisibleCells().map((cell) => (
                          <TableCell key={cell.id}>
                            {flexRender(cell.column.columnDef.cell, cell.getContext())}
                          </TableCell>
                        ))}
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </CardContent>
          </Card>
        </div>
      </div>
      <Dialog open={openEnableDisableModal} onOpenChange={setOpenEnableDisableModal}>
        <ConfirmationModal
          onCancel={() => {}}
          onConfirm={() => onSaveHandler(methodInfo)}
          data={{
            dialogTitle: "Confirmation",
            dialogSubtitle: `Are you sure you want to ${methodInfo.enable ? "enable" : "disable"} ${methodInfo.name} MFA?`,
          }}
          buttonState={{ confirm: { disable: isPending } }}
        />
      </Dialog>
    </>
  );
};
