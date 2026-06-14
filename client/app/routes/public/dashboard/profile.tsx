import { useEffect, useState } from "react";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { showErrorToast } from "@/hooks/use-toast";
import LogoLoadingSpinner from "@/components/loader-spinner/loader-spinner";

const IAM_APP = {
  key: "iam",
  label: "IAM",
  clientId: getRuntimeEnv("BLOCKS_IAM_CLIENT_ID"),
  redirectUri: getRuntimeEnv("BLOCKS_IAM_CALLBACK_URL"),
};

export function ProfilePage() {
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const launchIam = async () => {
      try {
        const blocksKey = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY");
        const idpBaseUrl = getRuntimeEnv("BLOCKS_IAM_BASE_URL");
        const initiateUrl = `${idpBaseUrl}/api/idp/initiate?x-blocks-key=${blocksKey}&clientId=${IAM_APP.clientId}&redirectUri=${IAM_APP.redirectUri}`;

        const headers: Record<string, string> = {};
        if (blocksKey) headers["X-Blocks-Key"] = blocksKey;

        const response = await fetch(initiateUrl, { headers });
        const data = await response.json();

        if (data.redirect_uri) {
          window.location.href = `${data.redirect_uri}/profile`;
        } else {
          setFailed(true);
          showErrorToast({ errors: "Failed to open profile" });
        }
      } catch (error) {
        console.error("IAM launch error:", error);
        setFailed(true);
        showErrorToast({ errors: "Unable to open profile. Please try again." });
      }
    };

    launchIam();
  }, []);

  if (failed) {
    return (
      <div className="flex min-h-[60vh] flex-col items-center justify-center gap-3 text-center">
        <p className="text-sm text-muted-foreground">
          Could not redirect to your profile.
        </p>
        <button
          onClick={() => window.location.reload()}
          className="text-sm font-medium text-primary underline"
        >
          Retry
        </button>
      </div>
    );
  }

  return <LogoLoadingSpinner />;
}