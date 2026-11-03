import { useMemo, useState } from "react";
import { AlertTriangle, FileJson, Info, Link2, Loader2 } from "lucide-react";
import { Alert, AlertDescription } from "@/components/ui-kits/alert/alert";
import { Button } from "@/components/ui-kits/button/button";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { cn } from "@/lib/utils";
import { usePreviewOpenApi } from "../hooks";
import { ProxyOpenApiOperation, ProxyOpenApiPreview, ProxyRoute } from "../types";
import { routeAddress } from "../utils";
import { ProxyMethodBadge } from "./proxy-method-badge";
import { renderRichText } from "@/lib/rich-text";
import { useT } from "@seliseblocks/genesis-os";

/** What the form should apply when the user confirms. Nothing is saved here. */
export type ProxyOpenApiImportResult = {
  /** New endpoints, in document order. Never a route that already exists. */
  routes: ProxyRoute[];
  /**
   * Security-scheme header names to add to the connection as empty credential rows. They belong to
   * the connection rather than to each endpoint because that is where this product keeps a
   * credential — one home, one value, never copied across every row that needs it.
   */
  credentialKeys: string[];
  /** The document's server URL when the user chose to adopt it, else `null`. */
  upstreamUrl: string | null;
};

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /**
   * The endpoints currently in the form. Collisions are judged against what is about to be saved,
   * not against what was saved: the form replaces the whole list, so a row added a minute ago
   * collides just as surely as one that has been there a year.
   */
  existingRoutes: ProxyRoute[];
  /** Keys already on the connection, so a scheme the credential already covers is not re-added. */
  existingCredentialKeys: string[];
  /** The connection's vendor URL as typed so far; `""` while it is still blank. */
  upstreamUrl: string;
  onImport: (result: ProxyOpenApiImportResult) => void;
};

type Source = "paste" | "url";

const hostOf = (url: string) => {
  try {
    return new URL(url).host;
  } catch {
    return "";
  }
};

const toRoute = (operation: ProxyOpenApiOperation): ProxyRoute => ({
  method: operation.method,
  path: operation.path,

  // The document's path is the vendor's own shape, so the two start identical. Keeping both is what
  // lets the client-facing path be renamed later without touching what the vendor expects.
  upstreamPath: operation.path,

  // Declared, never filled: a value out of a specification is an example at best and a credential at
  // worst. The rows arrive empty, ready for a {{$VAR.name}}.
  headers: operation.headerParameters.length
    ? operation.headerParameters.map((key) => ({ key, value: "" }))
    : null,
  query: operation.queryParameters.length
    ? operation.queryParameters.map((key) => ({ key, value: "" }))
    : null,
  bodyMerge: null,
  responseMode: null,
  responseInclude: null,
  resilience: null,
});

const renderMono = (children: string) => <span className="break-all font-mono">{children}</span>;

/** The document's errors (red) or warnings, as a list; nothing when there are none. */
const PreviewMessages = ({
  messages,
  variant,
}: Readonly<{ messages: string[]; variant?: "destructive" }>) => {
  if (!messages.length) return null;
  const Icon = variant === "destructive" ? AlertTriangle : Info;
  return (
    <Alert variant={variant}>
      <Icon className="h-4 w-4" />
      <AlertDescription>
        <ul className="list-disc space-y-0.5 pl-4 text-xs">
          {messages.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>
      </AlertDescription>
    </Alert>
  );
};

type PreviewOperation = ProxyOpenApiOperation & { alreadyExists: boolean };

/** One operation of the document: ticked to import it, disabled when its address is already taken. */
const OperationRow = ({
  operation,
  checked,
  onToggle,
}: Readonly<{
  operation: PreviewOperation;
  checked: boolean;
  onToggle: (on: boolean) => void;
}>) => {
  const { t } = useT("logic.proxy.components");
  const queryCount = operation.queryParameters.length;
  const headerCount = operation.headerParameters.length;
  let bringsText = t("PROXY_OPENAPI_IMPORT_DIALOG.BRINGS_HEADER", { count: headerCount });
  if (queryCount > 0 && headerCount > 0) {
    bringsText = t("PROXY_OPENAPI_IMPORT_DIALOG.BRINGS_BOTH", {
      query: queryCount,
      header: headerCount,
    });
  } else if (queryCount > 0) {
    bringsText = t("PROXY_OPENAPI_IMPORT_DIALOG.BRINGS_QUERY", { count: queryCount });
  }

  return (
    <li>
      <label
        className={cn(
          "flex items-start gap-2 rounded-md border p-2",
          operation.alreadyExists
            ? "cursor-not-allowed bg-muted/40"
            : "cursor-pointer hover:bg-muted/40",
        )}
      >
        <Checkbox
          className="mt-0.5"
          checked={checked && !operation.alreadyExists}
          disabled={operation.alreadyExists}
          aria-label={t("PROXY_OPENAPI_IMPORT_DIALOG.IMPORT", {
            method: operation.method,
            path: operation.path,
          })}
          onCheckedChange={(value) => onToggle(value === true)}
        />
        <span className="min-w-0 flex-1">
          <span className="flex flex-wrap items-center gap-2">
            <ProxyMethodBadge method={operation.method} />
            <code className="break-all font-mono text-xs">/{operation.path}</code>
            {operation.alreadyExists ? (
              <span className="text-[11px] text-muted-foreground">
                {t("PROXY_OPENAPI_IMPORT_DIALOG.ALREADY_DEFINED_KEPT_AS")}
              </span>
            ) : null}
          </span>
          {operation.summary ? (
            <span className="block text-[11px] text-muted-foreground">{operation.summary}</span>
          ) : null}
          {queryCount + headerCount > 0 ? (
            <span className="block text-[11px] text-muted-foreground">{bringsText}</span>
          ) : null}
        </span>
      </label>
    </li>
  );
};

type Translate = ReturnType<typeof useT>["t"];

/** What adopting the document's server URL does to the connection's current vendor URL. */
const replaceNote = (t: Translate, upstreamUrl: string, differentHost: boolean) => {
  if (upstreamUrl.trim() === "") return t("PROXY_OPENAPI_IMPORT_DIALOG.THE_CONNECTION_HAS_NO");
  if (differentHost)
    return t("PROXY_OPENAPI_IMPORT_DIALOG.THIS_REPLACES_WHICH_POINTS", { upstreamUrl });
  return t("PROXY_OPENAPI_IMPORT_DIALOG.THIS_REPLACES", { upstreamUrl });
};

/** Where the document comes from: pasted JSON or a URL Blocks fetches. */
const SpecSource = ({
  source,
  onSourceChange,
  specJson,
  onSpecJsonChange,
  specUrl,
  onSpecUrlChange,
}: Readonly<{
  source: Source;
  onSourceChange: (source: Source) => void;
  specJson: string;
  onSpecJsonChange: (value: string) => void;
  specUrl: string;
  onSpecUrlChange: (value: string) => void;
}>) => {
  const { t } = useT("logic.proxy.components");
  return (
    <>
      <div className="flex gap-2">
        {(
          [
            {
              value: "paste",
              label: t("PROXY_OPENAPI_IMPORT_DIALOG.PASTE_THE_DOCUMENT"),
              icon: FileJson,
            },
            { value: "url", label: t("PROXY_OPENAPI_IMPORT_DIALOG.FETCH_FROM_A_URL"), icon: Link2 },
          ] as const
        ).map((option) => (
          <Button
            key={option.value}
            type="button"
            size="xs"
            variant={source === option.value ? "default" : "outline"}
            className="gap-1.5"
            onClick={() => onSourceChange(option.value)}
          >
            <option.icon className="h-3.5 w-3.5" />
            {option.label}
          </Button>
        ))}
      </div>

      {source === "paste" ? (
        <div>
          <Label htmlFor="openapi-spec" className="text-xs">
            {t("PROXY_OPENAPI_IMPORT_DIALOG.OPENAPI_3_DOCUMENT_JSON")}
          </Label>
          <Textarea
            id="openapi-spec"
            rows={7}
            className="font-mono text-xs"
            // eslint-disable-next-line no-restricted-syntax -- example JSON, not prose
            placeholder='{ "openapi": "3.0.0", "paths": { … } }'
            value={specJson}
            onChange={(event) => onSpecJsonChange(event.target.value)}
          />
        </div>
      ) : (
        <div>
          <Label htmlFor="openapi-url" className="text-xs">
            {t("PROXY_OPENAPI_IMPORT_DIALOG.DOCUMENT_URL")}
          </Label>
          <Input
            id="openapi-url"
            className="h-9 text-xs"
            // eslint-disable-next-line no-restricted-syntax -- example URL, not prose
            placeholder="https://api.vendor.com/openapi.json"
            value={specUrl}
            onChange={(event) => onSpecUrlChange(event.target.value)}
          />
          <p className="mt-1 text-[11px] text-muted-foreground">
            {t("PROXY_OPENAPI_IMPORT_DIALOG.BLOCKS_FETCHES_IT_FROM")}
          </p>
        </div>
      )}
    </>
  );
};

/** "N of M operations can be added" with a button that ticks or clears them all. */
const OperationsHeader = ({
  importable,
  count,
  clashes,
  allChosen,
  onSelectAll,
}: Readonly<{
  importable: number;
  count: number;
  clashes: number;
  allChosen: boolean;
  onSelectAll: (all: boolean) => void;
}>) => {
  const { t } = useT("logic.proxy.components");
  return (
    <div className="flex items-center justify-between gap-3">
      <p className="text-xs font-medium">
        {t("PROXY_OPENAPI_IMPORT_DIALOG.OPERATIONS_CAN_BE_ADDED", { importable, count })}
        {clashes ? t("PROXY_OPENAPI_IMPORT_DIALOG.ALREADY_DEFINED", { clashes }) : ""}
      </p>
      <Button
        type="button"
        size="xs"
        variant="ghost"
        disabled={importable === 0}
        onClick={() => onSelectAll(!allChosen)}
      >
        {allChosen
          ? t("PROXY_OPENAPI_IMPORT_DIALOG.CLEAR_ALL")
          : t("PROXY_OPENAPI_IMPORT_DIALOG.SELECT_ALL")}
      </Button>
    </div>
  );
};

/**
 * Import endpoints from an OpenAPI document.
 *
 * The dialog only ever proposes. What the user ticks is added to the form they are already editing
 * and saved by the ordinary Save, so an import goes through the same validation, versioning and audit
 * as a route typed by hand — and can be reviewed, edited or abandoned before any of that.
 */
export const ProxyOpenApiImportDialog = ({
  open,
  onOpenChange,
  existingRoutes,
  existingCredentialKeys,
  upstreamUrl,
  onImport,
}: Props) => {
  const { t } = useT("logic.proxy.components");
  const [source, setSource] = useState<Source>("paste");
  const [specJson, setSpecJson] = useState("");
  const [specUrl, setSpecUrl] = useState("");
  const [preview, setPreview] = useState<ProxyOpenApiPreview | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [adoptBaseUrl, setAdoptBaseUrl] = useState(false);

  const previewOpenApi = usePreviewOpenApi();

  /**
   * Closing clears the document. A reopened dialog starts from nothing rather than from a
   * specification the user can no longer see, which is the only state in which what is on screen and
   * what would be imported are certainly the same thing.
   */
  const close = () => {
    setSpecJson("");
    setSpecUrl("");
    setPreview(null);
    setSelected(new Set());
    setAdoptBaseUrl(false);
    onOpenChange(false);
  };

  /** The addresses the form already answers, including rows added in this editing session. */
  const taken = useMemo(
    () => new Set(existingRoutes.map((route) => routeAddress(route.method, route.path))),
    [existingRoutes],
  );

  const operations = useMemo(
    () =>
      (preview?.operations ?? []).map((operation) => ({
        ...operation,
        alreadyExists:
          operation.alreadyExists || taken.has(routeAddress(operation.method, operation.path)),
      })),
    [preview, taken],
  );

  const importable = useMemo(
    () => operations.filter((operation) => !operation.alreadyExists),
    [operations],
  );
  const chosen = useMemo(
    () => importable.filter((operation) => selected.has(operation.operationId)),
    [importable, selected],
  );
  const clashes = operations.length - importable.length;

  const covered = useMemo(
    () => new Set(existingCredentialKeys.map((key) => key.trim().toLowerCase())),
    [existingCredentialKeys],
  );

  /** Security headers the chosen operations need that the connection does not already send. */
  const missingCredentials = useMemo(() => {
    const names = new Map<string, string>();
    chosen.forEach((operation) =>
      operation.securityHeaders.forEach((header) => {
        const key = header.trim().toLowerCase();
        if (key && !covered.has(key) && !names.has(key)) names.set(key, header.trim());
      }),
    );
    return [...names.values()];
  }, [chosen, covered]);

  const baseUrl = preview?.baseUrl ?? "";
  const differentHost =
    baseUrl !== "" && upstreamUrl !== "" && hostOf(baseUrl) !== hostOf(upstreamUrl);
  const baseUrlReplaceNote = replaceNote(t, upstreamUrl, differentHost);

  const read = async () => {
    const result = await previewOpenApi.mutateAsync(
      source === "url" ? { specUrl: specUrl.trim() } : { specJson },
    );
    setPreview(result);

    // Everything the document can contribute starts ticked: the user came here to import, and the
    // rows that cannot be imported are the ones already taken, which are never ticked.
    const fresh = result.operations.filter(
      (operation) =>
        !operation.alreadyExists && !taken.has(routeAddress(operation.method, operation.path)),
    );
    setSelected(new Set(fresh.map((operation) => operation.operationId)));
    setAdoptBaseUrl(upstreamUrl.trim() === "" && result.baseUrl !== "");
  };

  const toggle = (operationId: string, on: boolean) =>
    setSelected((current) => {
      const next = new Set(current);
      if (on) next.add(operationId);
      else next.delete(operationId);
      return next;
    });

  const confirm = () => {
    onImport({
      routes: chosen.map(toRoute),
      credentialKeys: missingCredentials,
      upstreamUrl: adoptBaseUrl && baseUrl ? baseUrl : null,
    });
    close();
  };

  const canRead = source === "url" ? specUrl.trim().length > 0 : specJson.trim().length > 0;

  return (
    <Dialog open={open} onOpenChange={(next) => (next ? onOpenChange(true) : close())}>
      <DialogContent className="max-h-[90vh] max-w-3xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>
            {t("PROXY_OPENAPI_IMPORT_DIALOG.IMPORT_ENDPOINTS_FROM_OPENAPI")}
          </DialogTitle>
          <DialogDescription>
            {t("PROXY_OPENAPI_IMPORT_DIALOG.NOTHING_IS_SAVED_HERE")}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3">
          <SpecSource
            source={source}
            onSourceChange={setSource}
            specJson={specJson}
            onSpecJsonChange={setSpecJson}
            specUrl={specUrl}
            onSpecUrlChange={setSpecUrl}
          />

          <Button
            type="button"
            size="sm"
            variant="outline"
            className="gap-1.5"
            disabled={!canRead || previewOpenApi.isPending}
            onClick={read}
          >
            {previewOpenApi.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : null}
            {t("PROXY_OPENAPI_IMPORT_DIALOG.READ_DOCUMENT")}
          </Button>
        </div>

        {preview ? (
          <div className="space-y-3">
            <PreviewMessages messages={preview.errors} variant="destructive" />

            <PreviewMessages messages={preview.warnings} />

            {baseUrl ? (
              <label className="flex cursor-pointer items-start gap-2 rounded-md border p-2">
                <Checkbox
                  className="mt-0.5"
                  checked={adoptBaseUrl}
                  aria-label={t("PROXY_OPENAPI_IMPORT_DIALOG.USE_THE_DOCUMENT_S")}
                  onCheckedChange={(checked) => setAdoptBaseUrl(checked === true)}
                />
                <span className="min-w-0 text-xs">
                  <span className="block font-medium">
                    {renderRichText(t("PROXY_OPENAPI_IMPORT_DIALOG.USE_0_0_AS", { baseUrl }), [
                      renderMono,
                    ])}
                  </span>
                  <span className="block text-[11px] text-muted-foreground">
                    {baseUrlReplaceNote}
                  </span>
                </span>
              </label>
            ) : null}

            {operations.length ? (
              <div>
                <OperationsHeader
                  importable={importable.length}
                  count={operations.length}
                  clashes={clashes}
                  allChosen={chosen.length === importable.length}
                  onSelectAll={(all) =>
                    setSelected(
                      all
                        ? new Set(importable.map((operation) => operation.operationId))
                        : new Set(),
                    )
                  }
                />

                <ul className="mt-2 max-h-64 space-y-1 overflow-y-auto pr-1">
                  {operations.map((operation) => (
                    <OperationRow
                      key={`${operation.method} ${operation.path}`}
                      operation={operation}
                      checked={selected.has(operation.operationId)}
                      onToggle={(on) => toggle(operation.operationId, on)}
                    />
                  ))}
                </ul>
              </div>
            ) : null}
            {!operations.length && !preview.errors.length && (
              <p className="text-xs text-muted-foreground">
                {t("PROXY_OPENAPI_IMPORT_DIALOG.THE_DOCUMENT_DESCRIBED_NO")}
              </p>
            )}

            {missingCredentials.length ? (
              <Alert>
                <Info className="h-4 w-4" />
                <AlertDescription className="text-xs">
                  {renderRichText(
                    t("PROXY_OPENAPI_IMPORT_DIALOG.AUTHENTICATED_WITH", {
                      names: missingCredentials.join(", "),
                    }),
                    [
                      (text) => <span className="font-mono">{text}</span>,
                      (text) => <span className="font-mono">{text}</span>,
                    ],
                  )}
                </AlertDescription>
              </Alert>
            ) : null}
          </div>
        ) : null}

        <DialogFooter>
          <Button type="button" variant="outline" onClick={close}>
            {t("common:CANCEL")}
          </Button>
          <Button type="button" disabled={chosen.length === 0} onClick={confirm}>
            {chosen.length
              ? t("PROXY_OPENAPI_IMPORT_DIALOG.ADD_N_ENDPOINTS", { count: chosen.length })
              : t("PROXY_OPENAPI_IMPORT_DIALOG.ADD_ENDPOINTS")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
