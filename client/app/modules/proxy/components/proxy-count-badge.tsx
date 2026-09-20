import { Badge } from "@/components/ui-kits/badge/badge";
import { useGetProxies } from "../hooks";

export const ProxyCountBadge = () => {
  // Only the total is needed here, so ask the server for the smallest possible page.
  const { data } = useGetProxies({ pageSize: 1 });
  const count = data?.totalCount ?? 0;

  return (
    <Badge
      variant="secondary"
      className="absolute -top-2 left-full ml-1 h-4 min-w-4 px-1 text-[9px] font-semibold text-primary"
    >
      {count}
    </Badge>
  );
};

