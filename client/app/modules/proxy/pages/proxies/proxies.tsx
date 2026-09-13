import { useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { useGetProxies } from "../../hooks";
import { ProxyList } from "../../components/proxy-list";
import { VariablesButton } from "../../components/variables-button";

export const Proxies = () => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { data, isLoading, isFetching } = useGetProxies();
  const proxies = data ?? [];
  const isListLoading = isLoading || isFetching;
  const shouldShowAddProxyButton = !isListLoading && proxies.length > 0;

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
        {shouldShowAddProxyButton && (
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
            <VariablesButton className="hidden" />
            <Button className="gap-2" onClick={() => navigate(scoped("proxy/new"))}>
              <Plus className="h-4 w-4" />
              Add proxy
            </Button>
          </div>
        )}
      </div>
      <Card>
        <CardContent>
          <ProxyList proxies={proxies} isLoading={isListLoading} />
        </CardContent>
      </Card>
    </section>
  );
};
