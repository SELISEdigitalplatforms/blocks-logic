import { useParams } from "react-router-dom";
import { TraceDetails } from "@blocks-lmt/components/trace-details";

export default function LmtTraceDetailsPage() {
  const { traceId } = useParams<{ traceId: string }>();

  return (
    <main className="flex flex-col gap-6 p-6">
      <TraceDetails id={traceId ?? ""} />
    </main>
  );
}
