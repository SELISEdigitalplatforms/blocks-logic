import { useProjectStore } from "@/store/useProjectStore";
import { SSOProviderList } from "./sso-provider-list";
import { GRANT_TYPES } from "@blocks-idp/authentication/constants/authentication.constant";
import { useGetAuthConfig } from "@blocks-idp/authentication/hooks/use-auth-config";

export const SSO = () => {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { data, isLoading } = useGetAuthConfig({ projectKey: tenantId });

  const isSocialSelected = data?.allowedGrantTypes.includes(GRANT_TYPES.social);
  return (
    <div>
      {!isLoading && !isSocialSelected && (
        <div className="text-blocks-error mb-4 flex flex-col items-center justify-center gap-1 rounded-sm border border-base-error bg-blocks-error-100 px-4 py-4 text-base font-normal text-blocks-error-800 md:flex-row">
          Please select the &apos;SSO&apos; grant type to configure social providers.
        </div>
      )}

      <div className="relative">
        <SSOProviderList />
        {!isLoading && !isSocialSelected && (
          <div className="absolute bottom-0 left-0 right-0 top-0 rounded-sm bg-border opacity-80"></div>
        )}
      </div>
    </div>
  );
};
