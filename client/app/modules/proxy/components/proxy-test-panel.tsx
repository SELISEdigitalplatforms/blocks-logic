import { useState } from "react";
import { Loader2, Send } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { Badge } from "@/components/ui-kits/badge/badge";
import { useSendProxyTestRequest } from "../hooks";
import { ProxyFormValues, ProxyMethod, ProxyTestResponse } from "../types";

type Props = {
  proxyId?: string;
  draft?: ProxyFormValues;
  method?: ProxyMethod;
};

export const ProxyTestPanel = ({ proxyId, draft, method = "GET" }: Props) => {
  const [pathSuffix, setPathSuffix] = useState("/");
  const [body, setBody] = useState("");
  const [response, setResponse] = useState<ProxyTestResponse | null>(null);
  const sendTest = useSendProxyTestRequest();

  const handleSend = async () => {
    const res = await sendTest.mutateAsync({
      proxyId,
      draft,
      method,
      pathSuffix,
      body,
      contentType: "application/json",
    });
    setResponse(res);
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Test it</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3 p-0">
        <Input
          value={pathSuffix}
          onChange={(event) => setPathSuffix(event.target.value)}
          placeholder="/charges"
        />
        <Textarea
          value={body}
          onChange={(event) => setBody(event.target.value)}
          placeholder="{ }"
        />
        <Button
          type="button"
          variant="outline"
          className="gap-2"
          onClick={handleSend}
          disabled={sendTest.isPending}
        >
          {sendTest.isPending ? (
            <Loader2 className="h-4 w-4 animate-spin" />
          ) : (
            <Send className="h-4 w-4" />
          )}
          Send test request
        </Button>
        {response ? (
          <div className="rounded-sm border bg-muted/20 p-3">
            <div className="flex items-center gap-2">
              <Badge variant={response.ok ? "success" : "error"}>
                {response.status} {response.statusText}
              </Badge>
              <span className="text-xs text-muted-foreground">{response.latencyMs}ms</span>
            </div>
            <p className="mt-2 text-sm text-muted-foreground">{response.meta}</p>
            <pre className="mt-2 max-h-44 overflow-auto rounded-sm bg-background p-3 text-xs">
              {response.responseBody}
            </pre>
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
};
