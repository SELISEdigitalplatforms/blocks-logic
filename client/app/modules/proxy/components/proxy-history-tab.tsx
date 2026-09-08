import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { cn } from "@/lib/utils";
import { useGetProxyActorNames, useGetProxyVersions, useRevertProxyVersion } from "../hooks";

const ProxyHistorySkeleton = () => (
  <Card className="rounded-xl px-6 py-1">
    <CardContent className="divide-y p-0">
      {Array.from({ length: 4 }).map((_, index) => (
        <div key={index} className="grid gap-4 py-6 sm:grid-cols-[1.25rem_1fr_auto]">
          <Skeleton className="mt-1 h-3.5 w-3.5 rounded-full" />
          <div className="space-y-3">
            <Skeleton className="h-5 w-full max-w-sm" />
            <Skeleton className="h-4 w-full max-w-md" />
            <Skeleton className="h-20 w-full" />
          </div>
          <Skeleton className="h-9 w-20 justify-self-start sm:justify-self-end" />
        </div>
      ))}
    </CardContent>
  </Card>
);

export const ProxyHistoryTab = ({ proxyId, active }: { proxyId: string; active: boolean }) => {
  const { data = [], isLoading } = useGetProxyVersions(active ? proxyId : undefined);
  const actorNames = useGetProxyActorNames(
    data.map((version) => version.actor),
    active && data.length > 0,
  );
  const revertVersion = useRevertProxyVersion();

  const handleRevert = async (versionId: string) => {
    const res = await revertVersion.mutateAsync({ proxyId, versionId });
    if (!res.isSuccess)
      return showErrorToast({
        errors: res.message || res.errors || "Unable to revert this proxy version.",
      });
    showSuccessToast({ description: "Proxy version reverted." });
  };

  if (isLoading) {
    return <ProxyHistorySkeleton />;
  }

  return (
    <Card className="rounded-xl px-6 py-1">
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
                {actorNames[version.actor] ?? version.actor} -{" "}
                {new Date(version.whenUtc).toLocaleString()} - {version.versionLabel}
              </p>
              {version.changes.length ? (
                <pre className="mt-4 whitespace-pre-wrap rounded-lg border bg-muted/20 p-4 text-sm">
                  {version.changes.map((change) => (
                    <div key={change.field}>
                      {change.before != null ? (
                        <span className="text-destructive">
                          {`- ${change.label}: ${change.before}\n`}
                        </span>
                      ) : null}
                      {change.after != null ? (
                        <span className="text-green-600 dark:text-green-500">
                          {`+ ${change.label}: ${change.after}`}
                        </span>
                      ) : null}
                    </div>
                  ))}
                </pre>
              ) : null}
            </div>
            {!["create", "delete"].includes(version.kind) ? (
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
