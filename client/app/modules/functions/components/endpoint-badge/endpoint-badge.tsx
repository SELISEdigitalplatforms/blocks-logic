import { useState } from "react";
import { Copy, Check } from "lucide-react";
import { useProjectStore, type IProject } from "@seliseblocks/genesis-os";
import { Button } from "@/components/ui-kits/button/button";
import { ProxyMethodBadge } from "@/modules/proxy/components/proxy-method-badge";
import {
  getFunctionClientPath,
  getFunctionClientUrl,
  toHttpVerb,
} from "../../constants/endpoint.constant";
import { HttpTriggerMethod } from "../../types/function.types";

/**
 * The function's public URL, published the way a proxy's is: the project's own API host when it
 * has one, on the public gateway path. A function is addressed by its id; the tenant is not in the
 * path — the caller's `x-blocks-key` names it, and the shared access authorizer resolves it.
 */
export const buildInvokeUrl = (functionId: string, project?: IProject | null) =>
  getFunctionClientUrl(project, functionId);

/**
 * The path half of {@link buildInvokeUrl}, for the places the design shows a path rather than a
 * full URL (the list's mono sub-line). Sharing the tail keeps the two from drifting apart.
 */
export const buildInvokePath = (functionId: string) => getFunctionClientPath(functionId);

/**
 * "Your client calls" for one function: the method the trigger answers, the URL, and a copy
 * button — the same block the proxy details page leads its Configuration card with. Anything the
 * caller appends after the id reaches the handler as `input.path`, which the trailing hint says.
 */
export const EndpointBadge = ({
  functionId,
  method,
}: {
  functionId: string;
  method: HttpTriggerMethod;
}) => {
  const [copied, setCopied] = useState(false);
  const project = useProjectStore().selectedProject;
  const url = buildInvokeUrl(functionId, project);

  const handleCopy = () => {
    navigator.clipboard.writeText(url);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="flex flex-col gap-2 rounded-lg border bg-muted/20 px-3 py-2.5">
      <div className="flex items-center gap-2">
        <ProxyMethodBadge method={toHttpVerb(method)} />
        <Button
          variant="ghost"
          size="icon"
          aria-label={copied ? "Endpoint copied" : "Copy endpoint"}
          className="ml-auto h-7 w-7 shrink-0 text-muted-foreground hover:text-foreground"
          onClick={handleCopy}
        >
          {copied ? (
            <Check className="h-3.5 w-3.5 text-green-500" />
          ) : (
            <Copy className="h-3.5 w-3.5" />
          )}
        </Button>
      </div>
      <p className="break-all font-mono text-sm text-foreground">
        {url}
        <span className="text-muted-foreground">/{"{path}"}</span>
      </p>
      <p className="text-xs text-muted-foreground">
        Whatever follows the id is yours to route on — it arrives as{" "}
        <code className="font-mono">input.path</code>, with the query, headers and body beside it.
        The other method is refused with <code className="font-mono">405</code>.
      </p>
    </div>
  );
};
