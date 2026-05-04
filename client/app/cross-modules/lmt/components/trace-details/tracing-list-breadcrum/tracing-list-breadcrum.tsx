import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui-kits/breadcrumb/breadcrumb";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Fragment, useContext } from "react";
import { timelineContext } from "../trace-details";

const LoadingSkelton = () => {
  return <Skeleton className="h-7 w-1/2" />;
};

export const TracingListBreadCrumb = () => {
  const {
    traceHistory: history,
    setTraceHistory,
    setSelectedTrace,
    isLoading,
  } = useContext(timelineContext);

  const onChange = (index: number) => {
    const sliced = history?.slice(0, index + 1);
    setTraceHistory(sliced);
    setSelectedTrace(() => sliced[sliced.length - 1].current);
  };

  if (isLoading) return <LoadingSkelton />;
  if (!history) return <LoadingSkelton />;
  return (
    <Breadcrumb className="hidden md:flex">
      <BreadcrumbList>
        {history.map((item, index) => (
          <Fragment key={item.root.spanId}>
            <BreadcrumbItem>
              {index === history.length - 1 ? (
                <BreadcrumbPage className="text-low-emphasis">{item?.root.spanId}</BreadcrumbPage>
              ) : (
                <BreadcrumbPage className="cursor-pointer" onClick={() => onChange(index)}>
                  {item?.root.spanId}
                </BreadcrumbPage>
              )}
            </BreadcrumbItem>
            {index < history.length - 1 && <BreadcrumbSeparator />}
          </Fragment>
        ))}
      </BreadcrumbList>
    </Breadcrumb>
  );
};
