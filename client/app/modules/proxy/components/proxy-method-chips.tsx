import { ProxyMethod } from "../types";
import { ProxyMethodBadge } from "./proxy-method-badge";

export const ProxyMethodChips = ({ methods }: { methods: ProxyMethod[] }) => (
  <div className="flex flex-wrap gap-1.5">
    {methods.map((method) => (
      <ProxyMethodBadge key={method} method={method} />
    ))}
  </div>
);

