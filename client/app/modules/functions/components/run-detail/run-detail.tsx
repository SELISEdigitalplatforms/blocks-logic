import { Loader2 } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Badge } from "@/components/ui-kits/badge/badge";
import { formatDate, parseDateString } from "@/lib/utils";
import { useGetRun, useGetRunLogs } from "../../hooks/use-runs";
import { RunStatusChip } from "../run-status-chip";

const MetricsStrip = ({
  durationMs,
  peakMemoryBytes,
  exitCode,
  attempt,
  maxAttempts,
}: {
  durationMs?: number | null;
  peakMemoryBytes?: number | null;
  exitCode?: number | null;
  attempt: number;
  maxAttempts: number;
}) => (
  <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
    {[
      { label: "Duration", value: durationMs != null ? `${durationMs} ms` : "-" },
      {
        label: "Peak memory",
        value: peakMemoryBytes != null ? `${(peakMemoryBytes / (1024 * 1024)).toFixed(1)} MB` : "-",
      },
      { label: "Exit code", value: exitCode ?? "-" },
      { label: "Attempt", value: `${attempt} / ${maxAttempts}` },
    ].map((metric) => (
      <div key={metric.label} className="rounded-lg border bg-muted/20 px-3 py-2">
        <p className="text-xs text-muted-foreground">{metric.label}</p>
        <p className="text-sm font-semibold">{metric.value}</p>
      </div>
    ))}
  </div>
);

const JsonPane = ({ title, value }: { title: string; value?: string | null }) => (
  <div className="space-y-1.5">
    <p className="text-xs font-medium text-muted-foreground">{title}</p>
    {value ? (
      <pre className="max-h-64 overflow-auto rounded-lg border bg-muted/20 p-3 font-mono text-xs">
        {value}
      </pre>
    ) : (
      <p className="rounded-lg border border-dashed px-3 py-4 text-center text-xs text-muted-foreground">
        None
      </p>
    )}
  </div>
);

const LogViewer = ({ runId }: { runId: string }) => {
  const { data, isLoading } = useGetRunLogs(runId);
  const logs = data?.data ?? [];

  if (isLoading) {
    return <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />;
  }
  if (logs.length === 0) {
    return <p className="text-xs text-muted-foreground">No logs recorded.</p>;
  }

  return (
    <div className="max-h-72 space-y-1 overflow-auto rounded-lg border bg-black/90 p-3 font-mono text-xs text-white">
      {logs.map((line) => (
        <div key={line.seq} className="flex gap-2">
          <span className="shrink-0 text-white/40">{formatDate(parseDateString(line.timestamp))}</span>
          <span
            className={
              line.level === "error"
                ? "text-red-400"
                : line.level === "warn"
                  ? "text-yellow-400"
                  : "text-white/90"
            }
          >
            {line.message}
          </span>
        </div>
      ))}
    </div>
  );
};

export const RunDetail = ({ runId }: { runId: string }) => {
  const { data: run, isLoading } = useGetRun({ runId });

  if (isLoading || !run) {
    return <Loader2 className="h-6 w-6 animate-spin text-primary" />;
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <RunStatusChip status={run.status} />
        <span className="font-mono text-xs text-muted-foreground">{run.id}</span>
        {run.errorCode && (
          <Badge variant="error" className="rounded-md px-2 py-0.5">
            {run.errorCode}
          </Badge>
        )}
      </div>

      {run.errorMessage && <p className="text-sm text-error">{run.errorMessage}</p>}

      <MetricsStrip
        durationMs={run.durationMs}
        peakMemoryBytes={run.peakMemoryBytes}
        exitCode={run.exitCode}
        attempt={run.attempt}
        maxAttempts={run.maxAttempts}
      />

      {run.attempts.length > 1 && (
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm">Attempts</CardTitle>
          </CardHeader>
          <CardContent className="space-y-2">
            {run.attempts.map((attempt) => (
              <div
                key={attempt.number}
                className="flex items-center justify-between rounded-md border px-3 py-2 text-sm"
              >
                <span>Attempt {attempt.number}</span>
                <RunStatusChip status={attempt.status} />
                {attempt.errorMessage && (
                  <span className="truncate text-xs text-muted-foreground">{attempt.errorMessage}</span>
                )}
              </div>
            ))}
          </CardContent>
        </Card>
      )}

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <JsonPane title="Input" value={run.input} />
        <JsonPane title="Result" value={run.result} />
      </div>

      {run.outputResults.length > 0 && (
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm">Output actions</CardTitle>
          </CardHeader>
          <CardContent className="space-y-2">
            {run.outputResults.map((outputResult) => (
              <div
                key={outputResult.actionId}
                className="flex items-center justify-between rounded-md border px-3 py-2 text-sm"
              >
                <span className="font-mono text-xs">{outputResult.actionId}</span>
                <Badge variant={outputResult.ok ? "success" : "error"} className="rounded-md px-2 py-0.5">
                  {outputResult.ok ? "OK" : "Failed"}
                  {outputResult.statusCode ? ` (${outputResult.statusCode})` : ""}
                </Badge>
                <span className="text-xs text-muted-foreground">{outputResult.durationMs} ms</span>
              </div>
            ))}
          </CardContent>
        </Card>
      )}

      <div className="space-y-1.5">
        <p className="text-xs font-medium text-muted-foreground">
          Logs {run.logsTruncated && "(truncated)"}
        </p>
        <LogViewer runId={run.id} />
      </div>
    </div>
  );
};
