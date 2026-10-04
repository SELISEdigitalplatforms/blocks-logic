import { Loader2, Send } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { Badge } from "@/components/ui-kits/badge/badge";
import { ProxyMethod, ProxyTestResponse } from "../types";
import { ProxyMethodBadge } from "./proxy-method-badge";

type Props = {
  pathSuffix: string;
  onPathSuffixChange: (value: string) => void;
  body: string;
  onBodyChange: (value: string) => void;
  onSend: () => void;
  sending: boolean;
  response: ProxyTestResponse | null;
  /** Optional heading override — the details page names the tab, not the card. */
  title?: string;
  description?: string;
  /** Methods the request may use. A single method renders as a badge instead of a picker. */
  methods?: ProxyMethod[];
  method?: ProxyMethod;
  onMethodChange?: (method: ProxyMethod) => void;
  /** Gateway path the suffix is appended to, rendered as a read-only prefix. */
  pathPrefix?: string;
  /** Hidden for methods that never carry a body. */
  showBody?: boolean;
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
 * Presentational Test panel, shared by the proxy form and the details page's Test tab. The Test
 * hook, its inputs (method / path / body) and its last result are owned by the caller so the
 * "Fill from test connection" button on the Response card can reuse the same inputs (SPEC §5.4).
 */
export const ProxyTestPanel = ({
  pathSuffix,
  onPathSuffixChange,
  body,
  onBodyChange,
  onSend,
  sending,
  response,
  title = "Test it",
  description,
  methods,
  method,
  onMethodChange,
  pathPrefix,
  showBody = true,
}: Props) => {
  const badge = response ? filterBadge(response.responseFilterNote) : null;
  const pathInput = (
    <Input
      value={pathSuffix}
      onChange={(event) => onPathSuffixChange(event.target.value)}
      placeholder="/charges"
      aria-label="Test request path"
      className={pathPrefix ? "border-0 focus-visible:ring-0 focus-visible:ring-offset-0" : ""}
    />
  );

  return (
    <Card>
      <CardHeader className="mb-4 gap-1">
        <CardTitle className="text-base">{title}</CardTitle>
        {description ? <CardDescription>{description}</CardDescription> : null}
      </CardHeader>
      <CardContent className="space-y-3 p-0">
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
          {methods?.length && method ? (
            methods.length > 1 && onMethodChange ? (
              <Select
                value={method}
                onValueChange={(value) => onMethodChange(value as ProxyMethod)}
              >
                <SelectTrigger className="h-10 w-full shrink-0 sm:w-[120px]" aria-label="Method">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {methods.map((item) => (
                    <SelectItem key={item} value={item}>
                      {item}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            ) : (
              <ProxyMethodBadge method={method} className="h-10 shrink-0 px-3 py-0 leading-10" />
            )
          ) : null}
          {pathPrefix ? (
            <div className="flex min-w-0 flex-1 items-center rounded-md border border-input bg-background focus-within:ring-2 focus-within:ring-ring focus-within:ring-offset-2">
              <span className="max-w-[55%] shrink-0 truncate border-r border-input px-3 py-2.5 font-mono text-xs text-muted-foreground">
                {pathPrefix}
              </span>
              {pathInput}
            </div>
          ) : (
            pathInput
          )}
        </div>
        {showBody ? (
          <Textarea
            value={body}
            onChange={(event) => onBodyChange(event.target.value)}
            placeholder="{ }"
            aria-label="Test request body"
          />
        ) : null}
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
