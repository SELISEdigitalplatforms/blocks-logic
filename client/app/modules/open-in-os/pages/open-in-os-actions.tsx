import { useParams } from "react-router";
import { getRuntimeEnv, useInitiateRedirect, useScopedPath, useT } from "@seliseblocks/genesis-os";
import { Button } from "@/components/ui-kits/button/button";
import { OS_ACTIONS_PATH, isProjectId, isUnset } from "../constants";

const Centered = ({ children }: { children: React.ReactNode }) => (
  <div className="flex min-h-[50vh] w-full flex-col items-center justify-center gap-4 px-4 text-center">
    {children}
  </div>
);

const CouldNotOpen = ({ canRetry }: { canRetry: boolean }) => {
  const { t } = useT("logic.shared");
  return (
    <Centered>
      <p role="alert" className="text-sm text-muted-foreground">
        {t("OPEN_IN_OS.COULD_NOT_OPEN")}
      </p>
      {canRetry ? (
        // A reload mints a new one-time sign-in URL; the failed one is never reused.
        <Button type="button" variant="outline" onClick={() => window.location.reload()}>
          {t("OPEN_IN_OS.TRY_AGAIN")}
        </Button>
      ) : null}
    </Centered>
  );
};

/** Asks IAM for a sign-in URL to OS (once per page load) and sends this tab there. */
const Redirect = ({ clientId, redirectUri, forwardedTo }: { clientId: string; redirectUri: string; forwardedTo: string }) => {
  const { t } = useT("logic.shared");
  const { error } = useInitiateRedirect({ clientId, redirectUri, forwardedTo });
  if (error) return <CouldNotOpen canRetry />;
  return (
    <Centered>
      <p role="status" className="text-sm text-muted-foreground">
        {t("OPEN_IN_OS.OPENING")}
      </p>
    </Centered>
  );
};

/**
 * Opened in a new tab from the workflow editor: signs the user in to Blocks OS through IAM and lands on
 * this project's Actions page. Each load asks IAM once, because every sign-in URL works only once.
 */
export const OpenInOsActions = () => {
  const { itemId } = useParams();
  const scoped = useScopedPath();
  const clientId = getRuntimeEnv("BLOCKS_OS_CLIENT_ID");
  const redirectUri = getRuntimeEnv("BLOCKS_OS_CALLBACK_URL");

  // Nothing to retry: a bad project id or a missing OS setting fails the same way every time.
  if (!isProjectId(itemId) || isUnset(clientId) || isUnset(redirectUri)) return <CouldNotOpen canRetry={false} />;

  return <Redirect clientId={clientId} redirectUri={redirectUri} forwardedTo={scoped(OS_ACTIONS_PATH)} />;
};
