import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { cn } from "@/lib/utils";
import { useGetProxyVersions, useRevertProxyVersion } from "../hooks";

export const ProxyHistoryTab = ({ proxyId, active }: { proxyId: string; active: boolean }) => {
  const { data = [] } = useGetProxyVersions(active ? proxyId : undefined);
  const revertVersion = useRevertProxyVersion();

  const handleRevert = async (versionId: string) => {
    const res = await revertVersion.mutateAsync({ proxyId, versionId });
    if (!res.isSuccess)
      return showErrorToast({ errors: res.errors || "Unable to revert this proxy version." });
    showSuccessToast({ description: "Proxy version reverted." });
  };

  return (
    <Card className="rounded-xl">
      <CardContent className="divide-y p-0">
        {!data.length ? (
          <p className="py-10 text-center text-sm text-muted-foreground">No change history yet.</p>
        ) : null}
        {data.map((version) => (
          <div key={version.id} className="grid gap-4 py-6 sm:grid-cols-[1.25rem_1fr_auto]">
            <span
              className={cn(
                "mt-1 h-3.5 w-3.5 rounded-full",
                version.kind === "create" ? "bg-green-500" : "bg-primary",
              )}
            />
            <div className="min-w-0">
              <h3 className="text-lg font-semibold">{version.summary.replace(/\.$/, "")}</h3>
              <p className="mt-1 text-sm text-muted-foreground">
                {version.actor} - {new Date(version.whenUtc).toLocaleString()} -{" "}
                {version.versionLabel}
              </p>
              {version.before || version.after ? (
                <pre className="mt-4 whitespace-pre-wrap rounded-lg border bg-muted/20 p-4 text-sm">
                  {version.before ? `- ${version.before}` : null}
                  {version.before && version.after ? "\n" : null}
                  {version.after ? `+ ${version.after}` : null}
                </pre>
              ) : null}
            </div>
            {version.kind !== "delete" ? (
              <Button
                variant="outline"
                size="sm"
                className="w-fit justify-self-start sm:justify-self-end"
                onClick={() => handleRevert(version.id)}
              >
                Revert
              </Button>
            ) : null}
          </div>
        ))}
      </CardContent>
    </Card>
  );
};
