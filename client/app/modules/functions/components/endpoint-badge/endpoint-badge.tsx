import { useState } from "react";
import { Copy, Check } from "lucide-react";
import { useProjectStore, type IProject } from "@seliseblocks/genesis-os";
import { Button } from "@/components/ui-kits/button/button";
import { showErrorToast } from "@/hooks/use-toast";
import { ProxyMethodBadge } from "@/modules/proxy/components/proxy-method-badge";
import {
  getFunctionClientPath,
  getFunctionClientUrl,
  snippetBlocksKey,
  toHttpVerb,
} from "../../constants/endpoint.constant";
import type { ProxyMethod } from "@/modules/proxy/types";
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
 * Below the URL, the one header every call must carry: `x-blocks-key`, without which the platform
 * cannot resolve the tenant and the call never reaches the function.
 */
export const EndpointBadge = ({
  functionId,
  method,
  methods,
}: {
  functionId: string;
  method: HttpTriggerMethod;
  /** Every verb the trigger accepts, when it lists several; replaces `method` on the badge. */
  methods?: ProxyMethod[];
}) => {
  const verbs = methods && methods.length > 0 ? methods : [toHttpVerb(method)];
  const [copied, setCopied] = useState(false);
  const [keyCopied, setKeyCopied] = useState(false);
  const project = useProjectStore().selectedProject;
  const url = buildInvokeUrl(functionId, project);
  const header = `x-blocks-key: ${snippetBlocksKey(project?.tenantId)}`;

  // The clipboard API rejects without a secure context or permission; say so instead of
  // showing "Copied" for text that never reached the clipboard.
  const copyText = async (text: string, setDone: (done: boolean) => void) => {
    try {
      await navigator.clipboard.writeText(text);
      setDone(true);
      setTimeout(() => setDone(false), 2000);
    } catch {
      showErrorToast({ title: "Could not copy", errors: ["Select the text and copy it by hand."] });
    }
  };

  const handleCopy = () => void copyText(url, setCopied);
  const handleCopyHeader = () => void copyText(header, setKeyCopied);

  return (
    <div className="flex flex-col gap-2 rounded-lg border bg-muted/20 px-3 py-2.5">
      <div className="flex items-center gap-2">
        {verbs.map((verb) => (
          <ProxyMethodBadge key={verb} method={verb} />
        ))}
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
      <div className="flex items-center gap-2 border-t pt-2">
        <span className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
          Header
        </span>
        <code
          className="break-all font-mono text-sm text-foreground"
          data-testid="blocks-key-header"
        >
          {header}
        </code>
        <Button
          variant="ghost"
          size="icon"
          aria-label={keyCopied ? "Header copied" : "Copy header"}
          className="ml-auto h-7 w-7 shrink-0 text-muted-foreground hover:text-foreground"
          onClick={handleCopyHeader}
        >
          {keyCopied ? (
            <Check className="h-3.5 w-3.5 text-green-500" />
          ) : (
            <Copy className="h-3.5 w-3.5" />
          )}
        </Button>
      </div>
      <p className="text-xs text-muted-foreground">
        Whatever follows the id is yours to route on — it arrives as{" "}
        <code className="font-mono">input.path</code>, with the query, headers and body beside it.
        Any other method is refused with{" "}
        <code className="font-mono">405</code>.
      </p>
    </div>
  );
};
