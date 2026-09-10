import { useState } from "react";
import { Copy, Check } from "lucide-react";
import { getRuntimeEnv } from "@seliseblocks/genesis-os";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { FUNCTION_INVOKE_ENDPOINT_BASE } from "../../constants/endpoint.constant";

/**
 * The function's public URL. A function is addressed by its id, and the tenant is not in the path
 * at all: Genesis' TenantValidationMiddleware resolves it from the caller's `x-blocks-key` header
 * for every path under `api`.
 */
export const buildInvokeUrl = (functionId: string) =>
  `${getRuntimeEnv("BLOCKS_LOGIC_BASE_URL") || ""}${buildInvokePath(functionId)}`;

/**
 * The path half of {@link buildInvokeUrl}, for the places the design shows a path rather than a
 * full URL (the list's mono sub-line). Sharing the tail keeps the two from drifting apart.
 */
export const buildInvokePath = (functionId: string) =>
  `${FUNCTION_INVOKE_ENDPOINT_BASE}/${functionId}`;

export const EndpointBadge = ({ functionId }: { functionId: string }) => {
  const [copied, setCopied] = useState(false);
  const url = buildInvokeUrl(functionId);

  const handleCopy = () => {
    navigator.clipboard.writeText(url);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="flex items-center gap-2 rounded-lg border bg-muted/20 px-3 py-2">
      <Badge variant="outline" className="font-mono text-xs uppercase">
        POST
      </Badge>
      <span className="min-w-0 flex-1 truncate font-mono text-xs">{url}</span>
      <Button
        variant="ghost"
        size="icon"
        aria-label={copied ? "Endpoint copied" : "Copy endpoint"}
        className="h-7 w-7 shrink-0 text-muted-foreground hover:text-foreground"
        onClick={handleCopy}
      >
        {copied ? (
          <Check className="h-3.5 w-3.5 text-green-500" />
        ) : (
          <Copy className="h-3.5 w-3.5" />
        )}
      </Button>
    </div>
  );
};
