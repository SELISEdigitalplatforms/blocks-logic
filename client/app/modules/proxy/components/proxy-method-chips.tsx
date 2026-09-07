import { Badge } from "@/components/ui-kits/badge/badge";
import { ProxyMethod } from "../types";

export const ProxyMethodChips = ({ methods }: { methods: ProxyMethod[] }) => (
  <div className="flex flex-wrap gap-1.5">
    {methods.map((method) => (
      <Badge key={method} variant="outline" className="font-mono text-[11px]">
        {method}
      </Badge>
    ))}
  </div>
);

