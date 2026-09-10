import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Info } from "lucide-react";

export const SandboxHelpCard = () => (
  <Card className="border-dashed bg-muted/20">
    <CardHeader className="flex-row items-center gap-2 pb-2">
      <Info className="h-4 w-4 text-muted-foreground" />
      <CardTitle className="text-sm">The function contract</CardTitle>
    </CardHeader>
    <CardContent className="space-y-2 text-xs text-muted-foreground">
      <p>
        Export a default async function: <code>export default async function handler(input, ctx)</code>.
      </p>
      <ul className="ml-4 list-disc space-y-1">
        <li>
          <code>input</code> — the JSON body the function was invoked with.
        </li>
        <li>
          <code>ctx.env</code> — your Variables, as plain strings.
        </li>
        <li>
          <code>ctx.run</code> — how this run was invoked (<code>invokedBy</code>, attempt, etc).
        </li>
        <li>
          <code>ctx.log.info/warn/error/debug(...)</code> — shows up in the run&apos;s logs.
        </li>
        <li>Whatever the function returns becomes the run&apos;s result (JSON, up to 5 MB).</li>
      </ul>
      <p>
        Secrets are never available inside the sandbox — reference them with{" "}
        <code>{"{{secret.NAME}}"}</code> in an output action instead.
      </p>
    </CardContent>
  </Card>
);
