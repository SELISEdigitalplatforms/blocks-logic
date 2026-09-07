import { RotateCcw } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useGetProxyVersions, useRevertProxyVersion } from "../hooks";

export const ProxyHistoryTab = ({ proxyId, active }: { proxyId: string; active: boolean }) => {
  const { data = [] } = useGetProxyVersions(active ? proxyId : undefined);
  const revertVersion = useRevertProxyVersion();

  const handleRevert = async (versionId: string) => {
    const res = await revertVersion.mutateAsync({ proxyId, versionId });
    if (!res.isSuccess) return showErrorToast({ errors: res.errors || "Unable to revert this proxy version." });
    showSuccessToast({ description: "Proxy version reverted." });
  };

  return (
    <Card>
      <CardContent className="space-y-3 p-0">
        {!data.length ? <p className="py-10 text-center text-sm text-muted-foreground">No change history yet.</p> : null}
        {data.map((version) => (
          <div key={version.id} className="rounded-sm border p-4">
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <div className="flex items-center gap-2">
                  <span className="font-semibold">{version.versionLabel}</span>
                  <span className="text-sm text-muted-foreground">{version.summary}</span>
                </div>
                <p className="mt-1 text-xs text-muted-foreground">
                  {version.actor} - {new Date(version.whenUtc).toLocaleString()}
                </p>
              </div>
              {version.kind !== "delete" ? (
                <Button variant="outline" size="xs" className="gap-1.5" onClick={() => handleRevert(version.id)}>
                  <RotateCcw className="h-3.5 w-3.5" />
                  Revert
                </Button>
              ) : null}
            </div>
            {version.before || version.after ? (
              <div className="mt-3 grid gap-2 text-xs md:grid-cols-2">
                <pre className="rounded-sm bg-muted/30 p-3">{version.before || "empty"}</pre>
                <pre className="rounded-sm bg-muted/30 p-3">{version.after || "empty"}</pre>
              </div>
            ) : null}
          </div>
        ))}
      </CardContent>
    </Card>
  );
};

