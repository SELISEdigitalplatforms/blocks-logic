import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { githubInfoService } from "@/cross-modules/devops/services/github-info.service";
import { Loader } from "lucide-react";

export default function CallbackPage() {
  const [searchParams] = useSearchParams();
  const code = searchParams.get("code");
  const state = searchParams.get("state");
  const [projectKey] = useState(() => localStorage.getItem("github_auth_project_key") || "");

  const { isLoading, isSuccess, isError } = useQuery({
    queryKey: ["github-verification", code, projectKey],
    queryFn: () => githubInfoService.verifyAuthorization(code || "", projectKey),
    enabled: !!code && !!projectKey,
    retry: false,
  });

  useEffect(() => {
    if (isSuccess) {
      // Signal the parent window that OAuth completed successfully
      localStorage.setItem("isReload", "true");
      
      // Clean up stored auth data
      localStorage.removeItem("github_auth_state");
      localStorage.removeItem("github_auth_project_key");
      localStorage.removeItem("github_auth_destination");
      
      // Close the popup window
      setTimeout(() => {
        window.close();
      }, 500);
    }
  }, [isSuccess]);

  if (!code || !state) {
    return (
      <div className="fixed inset-0 flex items-center justify-center bg-background/80 backdrop-blur-sm">
        <div className="flex flex-col items-center space-y-4">
          <p className="text-sm text-muted-foreground">Error: Invalid OAuth callback.</p>
        </div>
      </div>
    );
  }

  if (!projectKey) {
    return (
      <div className="fixed inset-0 flex items-center justify-center bg-background/80 backdrop-blur-sm">
        <div className="flex flex-col items-center space-y-4">
          <p className="text-sm text-destructive">Error: Project context missing.</p>
          <p className="text-xs text-muted-foreground">Please try again from the repositories page.</p>
          <button
            onClick={() => window.close()}
            className="text-sm text-muted-foreground hover:underline"
          >
            Close this window
          </button>
        </div>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="fixed inset-0 flex items-center justify-center bg-background/80 backdrop-blur-sm">
        <div className="flex flex-col items-center space-y-4">
          <Loader className="h-8 w-8 animate-spin" />
          <p className="text-sm text-muted-foreground">Verifying GitHub authorization...</p>
        </div>
      </div>
    );
  }

  if (isError) {
    return (
      <div className="fixed inset-0 flex items-center justify-center bg-background/80 backdrop-blur-sm">
        <div className="flex flex-col items-center space-y-4">
          <p className="text-sm text-destructive">GitHub authorization failed.</p>
          <button
            onClick={() => window.close()}
            className="text-sm text-muted-foreground hover:underline"
          >
            Close this window
          </button>
        </div>
      </div>
    );
  }

  if (isSuccess) {
    return (
      <div className="fixed inset-0 flex items-center justify-center bg-background/80 backdrop-blur-sm">
        <div className="flex flex-col items-center space-y-4">
          <p className="text-sm text-green-600">✓ GitHub authorization successful!</p>
          <p className="text-xs text-muted-foreground">This window will close automatically...</p>
        </div>
      </div>
    );
  }

  return null;
}
