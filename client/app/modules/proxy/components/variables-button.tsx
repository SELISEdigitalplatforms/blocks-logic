import { Variable } from "lucide-react";
import { getRuntimeEnv, usePrefetchRedirect, useScopedPath } from "@seliseblocks/genesis-os";
import { Button } from "@/components/ui-kits/button/button";
import { cn } from "@/lib/utils";

type Props = {
  className?: string;
};

/**
 * Sends the user to the OS "Secret management" page for the active project.
 *
 * Follows the AppSwitcher pattern: ask IAM to mint a validated OIDC redirect for
 * the OS client (prefetched on mount), then hand the browser off to it. The
 * `forwardedTo` path is the project-scoped secret-management route the user lands
 * on once OS finishes login — i.e. `/app/<projectId>/secret-management/secret`.
 */
export const VariablesButton = ({ className }: Props) => {
  const scoped = useScopedPath();

  const { isFetching, isReady, redirect } = usePrefetchRedirect({
    clientId: getRuntimeEnv("BLOCKS_OS_CLIENT_ID"),
    redirectUri: getRuntimeEnv("BLOCKS_OS_CALLBACK_URL"),
    forwardedTo: scoped("secret-management/secret"),
  });

  return (
    <Button
      type="button"
      variant="outline"
      className={cn("gap-2", className)}
      disabled={!isReady}
      onClick={redirect}
    >
      <Variable className="h-4 w-4" />
      {isFetching ? "Loading…" : "Variables"}
    </Button>
  );
};
