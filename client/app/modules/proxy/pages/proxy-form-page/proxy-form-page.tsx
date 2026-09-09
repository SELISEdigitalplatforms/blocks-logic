import { useEffect } from "react";
import { useLocation, useNavigate, useParams } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { showErrorToast } from "@/hooks/use-toast";
import { ProxyForm } from "../../components/proxy-form";
import { useGetProxyById } from "../../hooks";

export const ProxyFormPage = ({ mode }: { mode: "create" | "edit" }) => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { pathname } = useLocation();
  const params = useParams<{ proxyId?: string }>();
  const proxyId = params.proxyId;
  const isEdit = mode === "edit";
  const { data: proxy, isLoading, isFetched } = useGetProxyById(proxyId);

  useEffect(() => {
    if (isEdit && proxyId && isFetched && !isLoading && !proxy) {
      showErrorToast({ errors: "Proxy not found" });
      navigate(scoped("proxy"));
    } else if (isEdit && proxy?.name) {
      // 2nd breadcrumb position: show the proxy name instead of the raw id.
      BREADCRUMB_CUSTOM_TITLES[pathname.replace(/\/edit$/, "")] = proxy.name;
      BREADCRUMB_CUSTOM_TITLES[pathname] = "Edit";
    } else if (!isEdit) {
      BREADCRUMB_CUSTOM_TITLES[pathname] = "New Proxy";
    }
  }, [isEdit, isFetched, isLoading, navigate, pathname, proxy, proxyId, scoped]);

  const handleSuccess = (savedProxyId?: string) => {
    navigate(scoped(savedProxyId ? `proxy/${savedProxyId}` : "proxy"));
  };

  return (
    <div className="flex min-h-screen flex-col">
      <div className="px-6 pb-2 pt-4">
        <PageBreadcrumb breadcrumbIndex={3} />
      </div>
      <div className="flex-1 px-6 pb-8 pt-4">
        <ProxyForm
          mode={mode}
          proxy={proxy}
          isLoadingProxy={isEdit && isLoading}
          onSuccess={handleSuccess}
          onCancel={() => navigate(scoped(isEdit && proxyId ? `proxy/${proxyId}` : "proxy"))}
        />
      </div>
    </div>
  );
};
