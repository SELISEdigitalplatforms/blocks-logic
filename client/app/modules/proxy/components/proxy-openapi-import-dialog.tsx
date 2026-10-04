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

  const importable = operations.filter((operation) => !operation.alreadyExists);
  const chosen = importable.filter((operation) => selected.has(operation.operationId));
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
          <DialogTitle>Import endpoints from OpenAPI</DialogTitle>
          <DialogDescription>
            Nothing is saved here. The operations you pick are added to the form below, where you can
            edit them before saving as usual.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3">
          <div className="flex gap-2">
            {(
              [
                { value: "paste", label: "Paste the document", icon: FileJson },
                { value: "url", label: "Fetch from a URL", icon: Link2 },
              ] as const
            ).map((option) => (
              <Button
                key={option.value}
                type="button"
                size="xs"
                variant={source === option.value ? "default" : "outline"}
                className="gap-1.5"
                onClick={() => setSource(option.value)}
              >
                <option.icon className="h-3.5 w-3.5" />
                {option.label}
              </Button>
            ))}
          </div>

          {source === "paste" ? (
            <div>
              <Label htmlFor="openapi-spec" className="text-xs">
                OpenAPI 3 document (JSON)
              </Label>
              <Textarea
                id="openapi-spec"
                rows={7}
                className="font-mono text-xs"
                placeholder='{ "openapi": "3.0.0", "paths": { … } }'
                value={specJson}
                onChange={(event) => setSpecJson(event.target.value)}
              />
            </div>
          ) : (
            <div>
              <Label htmlFor="openapi-url" className="text-xs">
                Document URL
              </Label>
              <Input
                id="openapi-url"
                className="h-9 text-xs"
                placeholder="https://api.vendor.com/openapi.json"
                value={specUrl}
                onChange={(event) => setSpecUrl(event.target.value)}
              />
              <p className="mt-1 text-[11px] text-muted-foreground">
                Blocks fetches it from the server, through the same address checks as any vendor
                call. A URL that resolves inside a private network is refused.
              </p>
            </div>
          )}

          <Button
            type="button"
            size="sm"
            variant="outline"
            className="gap-1.5"
            disabled={!canRead || previewOpenApi.isPending}
            onClick={read}
          >
            {previewOpenApi.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : null}
            Read document
          </Button>
        </div>

        {preview ? (
          <div className="space-y-3">
            {preview.errors.length ? (
              <Alert variant="destructive">
                <AlertTriangle className="h-4 w-4" />
                <AlertDescription>
                  <ul className="list-disc space-y-0.5 pl-4 text-xs">
                    {preview.errors.map((message) => (
                      <li key={message}>{message}</li>
                    ))}
                  </ul>
                </AlertDescription>
              </Alert>
            ) : null}

            {preview.warnings.length ? (
              <Alert>
                <Info className="h-4 w-4" />
                <AlertDescription>
                  <ul className="list-disc space-y-0.5 pl-4 text-xs">
                    {preview.warnings.map((message) => (
                      <li key={message}>{message}</li>
                    ))}
                  </ul>
                </AlertDescription>
              </Alert>
            ) : null}

            {baseUrl ? (
              <label className="flex cursor-pointer items-start gap-2 rounded-md border p-2">
                <Checkbox
                  className="mt-0.5"
                  checked={adoptBaseUrl}
                  aria-label="Use the document's server as the vendor URL"
                  onCheckedChange={(checked) => setAdoptBaseUrl(checked === true)}
                />
                <span className="min-w-0 text-xs">
                  <span className="block font-medium">
                    Use <span className="break-all font-mono">{baseUrl}</span> as the vendor URL
                  </span>
                  <span className="block text-[11px] text-muted-foreground">
                    {upstreamUrl.trim() === ""
                      ? "The connection has no vendor URL yet."
                      : differentHost
                        ? `This replaces ${upstreamUrl}, which points somewhere else.`
                        : `This replaces ${upstreamUrl}.`}
                  </span>
                </span>
              </label>
            ) : null}

            {operations.length ? (
              <div>
                <div className="flex items-center justify-between gap-3">
                  <p className="text-xs font-medium">
                    {importable.length} of {operations.length}{" "}
                    {operations.length === 1 ? "operation" : "operations"} can be added
                    {clashes ? `, ${clashes} already defined` : ""}
                  </p>
                  <Button
                    type="button"
                    size="xs"
                    variant="ghost"
                    disabled={importable.length === 0}
                    onClick={() =>
                      setSelected(
                        chosen.length === importable.length
                          ? new Set()
                          : new Set(importable.map((operation) => operation.operationId)),
                      )
                    }
                  >
                    {chosen.length === importable.length ? "Clear all" : "Select all"}
                  </Button>
                </div>

                <ul className="mt-2 max-h-64 space-y-1 overflow-y-auto pr-1">
                  {operations.map((operation) => {
                    const checked = selected.has(operation.operationId);
                    const extras = [
                      operation.queryParameters.length
                        ? `${operation.queryParameters.length} query`
                        : null,
                      operation.headerParameters.length
                        ? `${operation.headerParameters.length} header`
                        : null,
                    ].filter(Boolean);

                    return (
                      <li key={`${operation.method} ${operation.path}`}>
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
                            aria-label={`Import ${operation.method} ${operation.path}`}
                            onCheckedChange={(value) =>
                              toggle(operation.operationId, value === true)
                            }
                          />
                          <span className="min-w-0 flex-1">
                            <span className="flex flex-wrap items-center gap-2">
                              <ProxyMethodBadge method={operation.method} />
                              <code className="break-all font-mono text-xs">
                                /{operation.path}
                              </code>
                              {operation.alreadyExists ? (
                                <span className="text-[11px] text-muted-foreground">
                                  already defined — kept as it is
                                </span>
                              ) : null}
                            </span>
                            {operation.summary ? (
                              <span className="block text-[11px] text-muted-foreground">
                                {operation.summary}
                              </span>
                            ) : null}
                            {extras.length ? (
                              <span className="block text-[11px] text-muted-foreground">
                                Brings {extras.join(" and ")}{" "}
                                {extras.length === 1 ? "parameter" : "parameters"}, with no values.
                              </span>
                            ) : null}
                          </span>
                        </label>
                      </li>
                    );
                  })}
                </ul>
              </div>
            ) : preview.errors.length ? null : (
              <p className="text-xs text-muted-foreground">
                The document described no operation this gateway can forward.
              </p>
            )}

            {missingCredentials.length ? (
              <Alert>
                <Info className="h-4 w-4" />
                <AlertDescription className="text-xs">
                  The document says these calls are authenticated with{" "}
                  <span className="font-mono">{missingCredentials.join(", ")}</span>. They will be
                  added to the connection with no value — set each one to a{" "}
                  <span className="font-mono">{"{{$VAR.name}}"}</span> before saving, or the vendor
                  will refuse the calls. A specification never carries the key itself.
                </AlertDescription>
              </Alert>
            ) : null}
          </div>
        ) : null}

        <DialogFooter>
          <Button type="button" variant="outline" onClick={close}>
            Cancel
          </Button>
          <Button type="button" disabled={chosen.length === 0} onClick={confirm}>
            {chosen.length
              ? `Add ${chosen.length} ${chosen.length === 1 ? "endpoint" : "endpoints"}`
              : "Add endpoints"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
