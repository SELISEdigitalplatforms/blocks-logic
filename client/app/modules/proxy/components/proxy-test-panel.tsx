import { Loader2, Send } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { Badge } from "@/components/ui-kits/badge/badge";
import { ProxyTestResponse } from "../types";

type Props = {
  pathSuffix: string;
  onPathSuffixChange: (value: string) => void;
  body: string;
  onBodyChange: (value: string) => void;
  onSend: () => void;
  sending: boolean;
  response: ProxyTestResponse | null;
};

/** Short badge for the response-filter outcome of a Test (SPEC §5.6). */
const filterBadge = (note: string | null | undefined) => {
  switch (note) {
    case "Applied":
    case "EmptyResult":
      return { label: "Filtered", variant: "success" as const };
    case "WholePrimitive":
      return { label: "Whole response", variant: "secondary" as const };
    case "Failed":
      return { label: "Filter failed — 502", variant: "error" as const };
    default:
      return null;
  }
};

/**
 * Presentational Test panel. The Test hook, its inputs (path / body) and its last result are owned
 * by {@link ProxyForm} so the "Fill from test connection" button on the Response card can reuse the
 * same inputs (SPEC §5.4).
 */
export const ProxyTestPanel = ({
  pathSuffix,
  onPathSuffixChange,
  body,
  onBodyChange,
  onSend,
  sending,
  response,
}: Props) => {
  const badge = response ? filterBadge(response.responseFilterNote) : null;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Test it</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3 p-0">
        <Input
          value={pathSuffix}
          onChange={(event) => onPathSuffixChange(event.target.value)}
          placeholder="/charges"
        />
        <Textarea
          value={body}
          onChange={(event) => onBodyChange(event.target.value)}
          placeholder="{ }"
        />
        <Button
          type="button"
          variant="outline"
          className="gap-2"
          onClick={onSend}
          disabled={sending}
        >
          {sending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
          Test Run
        </Button>
        {response ? (
          <div className="rounded-sm border bg-muted/20 p-3">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={response.ok ? "success" : "error"}>
                {response.status} {response.statusText}
              </Badge>
              {badge ? <Badge variant={badge.variant}>{badge.label}</Badge> : null}
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
