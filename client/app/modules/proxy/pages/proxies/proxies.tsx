import { useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Badge } from "@/components/ui-kits/badge/badge";
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
          <div className="flex items-center gap-2">
            <h1 className="text-2xl font-bold tracking-tight">Proxy</h1>
            <Badge variant="secondary" className="h-6 min-w-6 text-primary">
              {proxies.length}
            </Badge>
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            Route client calls through managed upstream configurations.
          </p>
        </div>
        <Button className="gap-2" onClick={() => navigate(scoped("proxy/new"))}>
          <Plus className="h-4 w-4" />
          Add proxy
        </Button>
      </div>
      <Card>
        <CardHeader className="mb-3">
          <h2 className="text-base font-semibold">Configurations</h2>
        </CardHeader>
        <CardContent>
          <ProxyList proxies={proxies} isLoading={isLoading || isFetching} />
        </CardContent>
      </Card>
    </section>
  );
};

