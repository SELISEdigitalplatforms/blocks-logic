import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetLogs } from "@blocks-lmt/hooks/use-log";
import {
  ColumnDef,
  flexRender,
  getCoreRowModel,
  getFilteredRowModel,
  useReactTable,
} from "@tanstack/react-table";
import { useContext, useMemo, useState } from "react";
import { timelineContext } from "../trace-details";
import { getLogFormatTimestamp, getLogLevelClassName } from "@blocks-lmt/utils";
import { ILog } from "@blocks-lmt/models/log.model";
import { FilterControls } from "@/components/filter-toolbar";

const LoadingSkelton = () => (
  <>
    {Array.from({ length: 10 }).map((_, index) => (
      <div key={index}>
        <Skeleton className="h-5 w-3/4 rounded" />
        <Skeleton className="my-2 h-5 w-full rounded" />
        <Skeleton className="h-5 w-full rounded-xl" />
      </div>
    ))}
  </>
);

const columns: ColumnDef<ILog>[] = [
  {
    id: "Trace",
    cell: ({ row }) => (
      <div className="flex items-center justify-between">
        <div className="flex flex-col">
          <div className="flex items-center gap-5">
            <span className="text-high-emphasis">
              {getLogFormatTimestamp(row.original.timestamp)}
            </span>
            <span className={`text-sm uppercase ${getLogLevelClassName(row.original.level)}`}>
              {row.original.level}
            </span>
          </div>
          <span className="text-sm text-warning-700">[{row.original.traceId}]</span>
          <div className="break-all text-justify text-sm text-medium-emphasis">
            {row.original.message}
          </div>
        </div>
      </div>
    ),
    filterFn: (_value) => {
      return true;
    },
  },
];

export const TracingLog = () => {
  const { traceHistory } = useContext(timelineContext);
  const {
    current: { traceId, spanId, serviceName },
  } = traceHistory[traceHistory?.length - 1];
  const [search, setSearch] = useState<string>("");
  const tenantId = useProjectStore().selectedProject?.tenantId || "";

  const { isLoading, isFetching, data } = useGetLogs({
    page: 0,
    pageSize: 200,
    serviceName,
    projectKey: tenantId,
    filter: {
      traceId: traceId,
      spanId: spanId,
    },
  });

  const logs = useMemo(() => {
    if (!data) return [];
    return data.data.filter((item: ILog) => item.message.toLowerCase().includes(search));
  }, [data, search]);

  const table = useReactTable({
    data: logs,
    columns,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
  });

  if (isLoading || isFetching) return <LoadingSkelton />;
  if (data?.data.length == 0)
    return <div className="flex h-64 items-center justify-center border">No data</div>;
  if (!data) return null;

  return (
    <div>
      <FilterControls.SearchInput
        value={search}
        onChange={setSearch}
        className="h-fit w-full py-2.5"
      />
      <div className="mt-4 h-[calc(100vh-375px)] overflow-hidden">
        <div className="flex h-full flex-col gap-6 overflow-auto">
          {table.getFilteredRowModel().rows.map((row) => (
            <div key={row.id} className="flex flex-wrap gap-6">
              {row
                .getVisibleCells()
                .map((cell) => flexRender(cell.column.columnDef.cell, cell.getContext()))}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};
