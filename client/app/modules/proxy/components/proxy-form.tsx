import { useEffect } from "react";
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
import { useCreateProxy, useDeleteProxy, useUpdateProxy } from "../hooks";
import { Proxy, ProxyFormValues, ProxyMethod } from "../types";
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
import { ProxyTestPanel } from "./proxy-test-panel";

const isBodyMethod = (method: ProxyMethod) =>
  method === "POST" || method === "PUT" || method === "PATCH";

type Props = {
  mode: "create" | "edit";
  proxy?: Proxy | null;
  isLoadingProxy?: boolean;
  onSuccess?: (proxyId?: string) => void;
  onCancel?: () => void;
  onDelete?: () => void;
};

export const ProxyForm = ({
  mode,
  proxy,
  isLoadingProxy,
  onSuccess,
  onCancel,
  onDelete,
}: Props) => {
  const isEdit = mode === "edit";
  const createProxy = useCreateProxy();
  const updateProxy = useUpdateProxy();
  const deleteProxy = useDeleteProxy();
  const isPending = createProxy.isPending || updateProxy.isPending || deleteProxy.isPending;

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
        isSecretRef: row.isSecretRef,
      })) ?? [],
    query:
      draft.query?.map((row) => ({
        key: row.key ?? "",
        value: row.value ?? "",
        isSecretRef: row.isSecretRef,
      })) ?? [],
    bodyMerge:
      draft.bodyMerge?.map((row) => ({
        key: row.key ?? "",
        value: row.value ?? "",
        isSecretRef: row.isSecretRef,
      })) ?? [],
    bodyMode: draft.bodyMode ?? "passthrough",
    methodConfigs: (draft.methodConfigs ?? []) as ProxyFormValues["methodConfigs"],
  };

  useEffect(() => {
    if (isEdit && proxy) {
      form.reset({
        name: proxy.name,
        upstreamUrl: proxy.upstreamUrl,
        methods: proxy.methods,
        headers: proxy.headers,
        query: proxy.query,
        bodyMerge: proxy.bodyMerge,
        bodyMode: proxy.bodyMerge.length ? "merge" : "passthrough",
        methodConfigs: proxy.methodConfigs ?? [],
      });
    } else if (!isEdit) {
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

  const handleDelete = async () => {
    if (!proxy || !window.confirm("Delete this proxy?")) return;
    const res = await deleteProxy.mutateAsync(proxy.id);
    if (!res.isSuccess) return showErrorToast({ errors: res.errors || "Failed to delete proxy" });
    showSuccessToast({ description: "Proxy deleted successfully." });
    onDelete?.();
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
        <ProxyFormHeader
          isEdit={isEdit}
          isPending={isPending}
          onCancel={onCancel}
          onDelete={handleDelete}
        />

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
                {selectedMethods[0] ?? "GET"} https://blocksapi.slsblx.com/logic/v4{clientPath}
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
              />
            </CardContent>
          </Card>
          {selectedMethods.some(isBodyMethod) ? (
            <ProxyRequestBodyCard control={form.control} />
          ) : null}
          {selectedMethods.length > 1 ? (
            <Card className="rounded-xl">
              <CardContent className="p-0">
                <ProxyMethodOverrides selectedMethods={selectedMethods} />
              </CardContent>
            </Card>
          ) : null}
          <ProxyTestPanel
            proxyId={proxy?.id}
            draft={draftValues}
            method={selectedMethods[0] ?? "GET"}
          />
        </div>
      </form>
    </Form>
  );
};
