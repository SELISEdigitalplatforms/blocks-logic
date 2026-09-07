import { useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { useGetProxies } from "../../hooks";
import { ProxyList } from "../../components/proxy-list";

export const Proxies = () => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { data, isLoading, isFetching } = useGetProxies();
  const proxies = data ?? [];

  return (
    <section className="flex flex-col gap-6 p-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Proxy</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Your client calls Blocks, Blocks adds the key and calls the third party. The vendor URL
            and secret never reach the browser.
          </p>
        </div>
        <Button className="gap-2" onClick={() => navigate(scoped("proxy/new"))}>
          <Plus className="h-4 w-4" />
          Add proxy
        </Button>
      </div>
      <div>
        <ProxyList proxies={proxies} isLoading={isLoading || isFetching} />
      </div>
    </section>
  );
};
