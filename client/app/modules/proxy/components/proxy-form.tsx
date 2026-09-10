import { useEffect, useRef, useState } from "react";
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
import { getProxyClientPath } from "../constants";
import { useCreateProxy, useSecrets, useSendProxyTestRequest, useUpdateProxy } from "../hooks";
import { Proxy, ProxyFormValues, ProxyMethod, ProxyTestResponse, SampleResult } from "../types";
import {
  compactKeyValues,
  proxyFormDefaultValues,
  proxyFormSchema,
  slugifyProxyName,
} from "../utils";
import { KeyValueFieldArray } from "./key-value-field-array";
import { ProxyFormHeader } from "./proxy-form-header";
import { ProxyMethodOverrides } from "./proxy-method-overrides";
import { ProxyMethodSelector } from "./proxy-method-selector";
import { ProxyRequestBodyCard } from "./proxy-request-body-card";
import { ProxyResponseCard } from "./proxy-response-card";
import { ProxyTestPanel } from "./proxy-test-panel";
import { getRuntimeEnv } from "@seliseblocks/genesis-os";

const isBodyMethod = (method: ProxyMethod) =>
  method === "POST" || method === "PUT" || method === "PATCH";

type Props = {
  mode: "create" | "edit";
  proxy?: Proxy | null;
  isLoadingProxy?: boolean;
  onSuccess?: (proxyId?: string) => void;
  onCancel?: () => void;
};

export const ProxyForm = ({ mode, proxy, isLoadingProxy, onSuccess, onCancel }: Props) => {
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
  const selectedMethods = useWatch({ control: form.control, name: "methods" });
  const clientPath = getProxyClientPath(slugifyProxyName(name));
  const draft = useWatch({ control: form.control });
  const draftValues: ProxyFormValues = {
    name: draft.name ?? "",
    upstreamUrl: draft.upstreamUrl ?? "",
    methods: draft.methods ?? ["GET"],
    headers:
      draft.headers?.map((row) => ({
        key: row.key ?? "",
        value: row.value ?? "",
      })) ?? [],
    query:
      draft.query?.map((row) => ({
        key: row.key ?? "",
        value: row.value ?? "",
      })) ?? [],
    bodyMerge:
      draft.bodyMerge?.map((row) => ({
        key: row.key ?? "",
        value: row.value ?? "",
      })) ?? [],
    bodyMode: draft.bodyMode ?? "passthrough",
    methodConfigs: (draft.methodConfigs ?? []) as ProxyFormValues["methodConfigs"],
    responseMode: draft.responseMode ?? "all",
    responseInclude: (draft.responseInclude ?? []).filter(Boolean),
  };

  // The Test panel's inputs live here so the Response card's "Fill from test connection" can
  // reuse them (SPEC §5.4). One Test hook instance backs both the normal Send and the sample run.
  const [testPathSuffix, setTestPathSuffix] = useState("/");
  const [testBody, setTestBody] = useState("");
  const [testResponse, setTestResponse] = useState<ProxyTestResponse | null>(null);
  const sendTest = useSendProxyTestRequest();
  const primaryMethod = selectedMethods?.[0] ?? "GET";

  const runTest = async () => {
    const res = await sendTest.mutateAsync({
      proxyId: proxy?.id,
      draft: proxy?.id ? undefined : draftValues,
      method: primaryMethod,
      pathSuffix: testPathSuffix,
      body: testBody,
      contentType: "application/json",
    });
    setTestResponse(res);
  };

  const runSample = async (): Promise<SampleResult> => {
    // Always send a draft with filtering forced off so the sample is the full response shape.
    const res = await sendTest.mutateAsync({
      draft: { ...draftValues, responseMode: "all", responseInclude: [] },
      method: primaryMethod,
      pathSuffix: testPathSuffix,
      body: testBody,
      contentType: "application/json",
    });
    // "Fill from test run" doubles as a Test — surface the raw result in the Test panel too.
    setTestResponse(res);
    return {
      ok: res.ok,
      status: res.status,
      contentType: res.contentType,
      body: res.responseBody,
      bytes: res.responseBodyBytes,
      error: res.ok ? undefined : res.meta,
    };
  };

  // Seed the form from the loaded proxy exactly once per identity. A bare `proxy` dependency
  // would re-run on every React Query background refetch (the query has no `staleTime`), and each
  // fresh reference would `form.reset` the user's in-progress edits back to the server values.
  const seededForId = useRef<string | null>(null);
  useEffect(() => {
    if (isEdit && proxy && seededForId.current !== proxy.id) {
      seededForId.current = proxy.id;
      form.reset({
        name: proxy.name,
        upstreamUrl: proxy.upstreamUrl,
        methods: proxy.methods,
        headers: proxy.headers,
        query: proxy.query,
        bodyMerge: proxy.bodyMerge,
        bodyMode: proxy.bodyMerge.length ? "merge" : "passthrough",
        methodConfigs: proxy.methodConfigs ?? [],
        responseMode: proxy.responseMode,
        responseInclude: proxy.responseInclude,
      });
    } else if (!isEdit && seededForId.current !== "new") {
      seededForId.current = "new";
      form.reset(proxyFormDefaultValues);
    }
  }, [form, isEdit, proxy]);

  const toggleMethod = (method: ProxyMethod, checked: boolean) => {
    const next = checked ? [method] : selectedMethods.filter((item) => item !== method);
    if (!next.length) {
      form.setError("methods", { message: "Select at least one method." });
      return;
    }
    form.clearErrors("methods");
    form.setValue("methods", next, { shouldDirty: true, shouldValidate: true });
  };

  const handleSubmit = async (values: ProxyFormValues) => {
    const payload = {
      ...values,
      headers: compactKeyValues(values.headers),
      query: compactKeyValues(values.query),
    };

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
        <ProxyFormHeader isEdit={isEdit} isPending={isPending} onCancel={onCancel} />

        <Card className="rounded-xl">
          <CardContent className="space-y-6 p-0">
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
                    <FormLabel>Third-party endpoint</FormLabel>
                    <FormControl>
                      <Input
                        type="text"
                        inputMode="url"
                        placeholder="Enter third-party endpoint"
                        {...field}
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </div>
            <ProxyMethodSelector
              control={form.control}
              selectedMethods={selectedMethods}
              onToggle={toggleMethod}
            />
            <div className="space-y-2 rounded-lg border border-primary/25 bg-primary/5 p-4 text-primary">
              <span className="text-xs font-semibold uppercase tracking-wide">
                Your client calls this
              </span>
              <p className="break-all font-mono text-sm">
                {selectedMethods[0] ?? "GET"} {getRuntimeEnv("BLOCKS_LOGIC_BASE_URL")}
                {clientPath}
              </p>
              <p className="text-sm text-primary/80">
                Send X-Blocks-Key. Path, body and extra query string pass straight through.
              </p>
            </div>
          </CardContent>
        </Card>

        <div className="grid gap-6">
          <Card className="rounded-xl">
            <CardContent className="p-0">
              <KeyValueFieldArray
                control={form.control}
                name="headers"
                label="Header rows"
                addLabel="Add header"
                {...variableProps}
              />
            </CardContent>
          </Card>
          <Card className="rounded-xl">
            <CardContent className="p-0">
              <KeyValueFieldArray
                control={form.control}
                name="query"
                label="Query parameter rows"
                addLabel="Add query"
                {...variableProps}
              />
            </CardContent>
          </Card>
          {selectedMethods.some(isBodyMethod) ? (
            <ProxyRequestBodyCard control={form.control} {...variableProps} />
          ) : null}
          <ProxyResponseCard
            control={form.control}
            draft={draftValues}
            method={primaryMethod}
            runSample={runSample}
            seedKey={proxy?.id ?? "new"}
          />
          {selectedMethods.length > 1 ? (
            <Card className="rounded-xl">
              <CardContent className="p-0">
                <ProxyMethodOverrides
                  selectedMethods={selectedMethods}
                  {...variableProps}
                />
              </CardContent>
            </Card>
          ) : null}
          <ProxyTestPanel
            pathSuffix={testPathSuffix}
            onPathSuffixChange={setTestPathSuffix}
            body={testBody}
            onBodyChange={setTestBody}
            onSend={runTest}
            sending={sendTest.isPending}
            response={testResponse}
          />
        </div>
      </form>
    </Form>
  );
};
