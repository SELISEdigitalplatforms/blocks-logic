import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { Copy } from "lucide-react";
import { buildInvokeUrl } from "../endpoint-badge";
import { ITriggerConfig } from "../../types/function.types";

type InvokeSnippetCardProps = {
  functionId: string;
  trigger: ITriggerConfig;
};

export const InvokeSnippetCard = ({ functionId, trigger }: InvokeSnippetCardProps) => {
  const url = buildInvokeUrl(functionId);
  // x-blocks-key is not optional: it is how the platform resolves the tenant for anything under
  // /api, so a caller without it never reaches the function. It identifies the project, it does
  // not authenticate a user — that is what the Authorization header below is for on a
  // token-protected trigger.
  const authHeader =
    trigger.authMode === "Token" ? ` \\\n  -H "Authorization: Bearer <token>"` : "";
  const snippet = `curl -X POST "${url}?wait=true" \\\n  -H "x-blocks-key: <your project key>"${authHeader} \\\n  -H "Content-Type: application/json" \\\n  -d '{"key":"value"}'`;

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between pb-3">
        <CardTitle className="text-base">Invoke</CardTitle>
        <Button
          variant="outline"
          size="sm"
          className="gap-1.5 text-xs"
          onClick={() => navigator.clipboard.writeText(snippet)}
        >
          <Copy className="h-3.5 w-3.5" />
          Copy
        </Button>
      </CardHeader>
      <CardContent>
        <pre className="overflow-x-auto rounded-lg border bg-muted/30 p-3 font-mono text-xs">
          {snippet}
        </pre>
        <p className="mt-2 text-xs text-muted-foreground">
          Omit <code>?wait=true</code> to get a run id back immediately instead of waiting for the
          result.
        </p>
      </CardContent>
    </Card>
  );
};
