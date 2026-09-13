import { type ReactNode, useEffect, useRef } from "react";
import { useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2 } from "lucide-react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Input } from "@/components/ui-kits/input/input";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { getProxyClientUrl } from "../constants";
import { useCreateProxy, useSecrets, useSendProxyTestRequest, useUpdateProxy } from "../hooks";
import { Proxy, ProxyFormValues, ProxyRoute, ProxyTestResponse } from "../types";
import {
  deriveProxyMethods,
  proxyFormDefaultValues,
  proxyFormSchema,
  slugifyProxyName,
  splitCredentialRows,
  toCredentialRows,
} from "../utils";
import { KeyValueFieldArray } from "./key-value-field-array";
import { ProxyFormHeader } from "./proxy-form-header";
import { ProxyRoutesCard, blankRoute } from "./proxy-routes-card";
import { useProjectStore } from "@seliseblocks/genesis-os";

type Props = {
  mode: "create" | "edit";
  proxy?: Proxy | null;
  isLoadingProxy?: boolean;
  headerContent?: ReactNode;
  onSuccess?: (proxyId?: string) => void;
  onCancel?: () => void;
};

const sameMethods = (a: readonly string[], b: readonly string[]) =>
  a.length === b.length && a.every((method, i) => method === b[i]);

/**
 * A proxy is a Connection to a vendor exposing one or more Endpoints. The Connection holds what is
 * genuinely shared — the base URL and the credential — and every per-call setting lives on the
 * endpoint that makes the call. The single-endpoint proxy is just the case where there is one row.
 *
 * `methods` is derived from the endpoints rather than edited: the server needs the list for the
 * 405 `Allow` header, but there is nothing for the user to decide that the endpoints do not already
 * say. Body-merge and response filtering exist only per endpoint, so the proxy-level fields are sent
 * empty; `methodConfigs` is superseded by endpoints and sent empty too.
 */
export const ProxyForm = ({
  mode,
  proxy,
  isLoadingProxy,
  headerContent,
  onSuccess,
  onCancel,
}: Props) => {
  const isEdit = mode === "edit";
  const createProxy = useCreateProxy();
  const updateProxy = useUpdateProxy();
  const isPending = createProxy.isPending || updateProxy.isPending;

  // The tenant's `{{$VAR.name}}` picker list — loaded once and prop-drilled into every value field.
  const secretsQuery = useSecrets();
  const variableProps = {
    variables: secretsQuery.data ?? [],
    variablesLoading: secretsQuery.isLoading,
    variablesError: secretsQuery.isError,
  };

  const form = useForm<ProxyFormValues>({
    defaultValues: proxyFormDefaultValues,
    resolver: zodResolver(proxyFormSchema),
  });

  const name = useWatch({ control: form.control, name: "name" });
  const upstreamUrl = useWatch({ control: form.control, name: "upstreamUrl" }) ?? "";
  const watchedRoutes = useWatch({ control: form.control, name: "routes" }) as ProxyRoute[] | undefined;
  const selectedProject = useProjectStore().selectedProject;
  const slug = slugifyProxyName(name);
  const clientUrlFor = (routePath: string) => getProxyClientUrl(selectedProject, slug, routePath);

  // Keep the derived method list in step with the endpoints, so validation and the payload agree.
  useEffect(() => {
    const derived = deriveProxyMethods(watchedRoutes ?? []);
    if (!sameMethods(form.getValues("methods") ?? [], derived)) {
      form.setValue("methods", derived, { shouldDirty: true });
    }
  }, [form, watchedRoutes]);

  /** The current form state in the shape the API and the Test endpoint expect. */
  const toApiValues = (values: ProxyFormValues): ProxyFormValues => {
    const { headers, query } = splitCredentialRows(values.credentials);
    return {
      ...values,
      name: values.name ?? "",
      upstreamUrl: values.upstreamUrl ?? "",
      headers,
      query,
      methods: deriveProxyMethods(values.routes ?? []),
      routes: values.routes ?? [],
      // Per-endpoint only; nothing at proxy level.
      bodyMerge: [],
      bodyMode: "passthrough",
      methodConfigs: [],
      responseMode: "all",
      responseInclude: [],
    };
  };

  const sendTest = useSendProxyTestRequest();
  const runTest = (route: ProxyRoute, pathSuffix: string, body: string): Promise<ProxyTestResponse> =>
    // Always the draft, never the saved proxy: the point of Test is to check what you are about
    // to save.
    sendTest.mutateAsync({
      draft: toApiValues(form.getValues()),
      method: route.method,
      pathSuffix,
      body,
      contentType: "application/json",
    });

  // Seed the form from the loaded proxy exactly once per identity. A bare `proxy` dependency
  // would re-run on every React Query background refetch (the query has no `staleTime`), and each
  // fresh reference would `form.reset` the user's in-progress edits back to the server values.
  const seededForId = useRef<string | null>(null);
  useEffect(() => {
    if (isEdit && proxy && seededForId.current !== proxy.id) {
      seededForId.current = proxy.id;
      const routes = proxy.routes?.length ? proxy.routes : [blankRoute(proxy.methods[0] ?? "GET")];
      form.reset({
        ...proxyFormDefaultValues,
        name: proxy.name,
        upstreamUrl: proxy.upstreamUrl,
        methods: deriveProxyMethods(routes),
        headers: proxy.headers,
        query: proxy.query,
        credentials: toCredentialRows(proxy.headers, proxy.query),
        routes,
      });
    } else if (!isEdit && seededForId.current !== "new") {
      seededForId.current = "new";
      form.reset(proxyFormDefaultValues);
    }
  }, [form, isEdit, proxy]);

  const handleSubmit = async (values: ProxyFormValues) => {
    const payload = toApiValues(values);

    if (isEdit && proxy) {
      const res = await updateProxy.mutateAsync({ id: proxy.id, values: payload });
      if (!res.isSuccess) return showErrorToast({ errors: res.errors || "Failed to save proxy" });
      showSuccessToast({ description: "Proxy updated successfully." });
      onSuccess?.(proxy.id);
      return;
    }

    const res = await createProxy.mutateAsync(payload);
    if (!res.isSuccess) return showErrorToast({ errors: res.errors || "Failed to create proxy" });
    showSuccessToast({ description: "Proxy created successfully." });
    onSuccess?.(res.itemId);
  };

  if (isLoadingProxy) {
    return (
      <div className="flex min-h-[400px] items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
      </div>
    );
  }

  return (
    <Form {...form}>
      <form noValidate onSubmit={form.handleSubmit(handleSubmit)} className="space-y-6">
        <div className="sticky top-0 z-30 -mx-6 -mt-4 bg-surface-app px-6 pb-4 pt-4 shadow-sm">
          {headerContent ? <div className="pb-6">{headerContent}</div> : null}
          <ProxyFormHeader isEdit={isEdit} isPending={isPending} onCancel={onCancel} />
        </div>

        {/* Connection: the vendor, and the credential that never reaches the client. */}
        <Card className="rounded-xl !mt-0">
          <CardContent className="space-y-6 p-0">
            <div>
              <p className="text-sm font-semibold">Connection</p>
              <p className="text-xs text-muted-foreground">
                The vendor this proxy talks to, and what every request to it carries.
              </p>
            </div>
            <div className="grid gap-5 lg:grid-cols-[minmax(260px,0.8fr)_minmax(320px,1.2fr)]">
              <FormField
                control={form.control}
                name="name"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Name</FormLabel>
                    <FormControl>
                      <Input placeholder="Enter name" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="upstreamUrl"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Vendor base URL</FormLabel>
                    <FormControl>
                      <Input
                        type="text"
                        inputMode="url"
                        placeholder="https://api.vendor.com"
                        {...field}
                      />
                    </FormControl>
                    <p className="text-xs text-muted-foreground">
                      Endpoint paths below are appended to this.
                    </p>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </div>
            <KeyValueFieldArray
              control={form.control}
              name="credentials"
              label="Sent with every request"
              addLabel="Add"
              sendAsColumn
              description={
                <>
                  The credential lives here, never in your client. Headers cover almost every
                  vendor; choose a query parameter only when the vendor takes its key in the URL.
                </>
              }
              {...variableProps}
            />
          </CardContent>
        </Card>

        {/* Endpoints: every per-call setting, on the call it belongs to. */}
        <Card className="rounded-xl">
          <CardContent className="p-4">
            <ProxyRoutesCard
              upstreamUrl={upstreamUrl}
              clientUrlFor={clientUrlFor}
              onTest={runTest}
              {...variableProps}
            />
          </CardContent>
        </Card>
      </form>
    </Form>
  );
};
