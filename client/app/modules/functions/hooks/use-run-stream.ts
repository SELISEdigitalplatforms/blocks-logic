import { useEffect, useRef, useState } from "react";
import { functionService } from "../services/function.service";
import { readSse } from "../utils/sse";
import { tStatic } from "@seliseblocks/genesis-os";

/** Most of a streamed answer the panel keeps on screen; the run's result keeps its start anyway. */
export const RUN_STREAM_MAX_CHARS = 1024 * 1024;

export type RunStreamState = "idle" | "streaming" | "done" | "error";

export interface IRunStream {
  /** The pieces received so far, joined. */
  text: string;
  state: RunStreamState;
  /** Set when the run failed after it started streaming (`event: error`). */
  error?: string;
  /** The text passed {@link RUN_STREAM_MAX_CHARS}; the rest was not kept on screen. */
  clipped: boolean;
}

const IDLE: IRunStream = { text: "", state: "idle", clipped: false };

const errorMessageOf = (data: string) => {
  try {
    const parsed = JSON.parse(data) as { message?: unknown; errorCode?: unknown };
    if (typeof parsed.message === "string" && parsed.message) return parsed.message;
    if (typeof parsed.errorCode === "string") return parsed.errorCode;
  } catch {
    // Not JSON: the text itself is the best message there is.
  }
  return data || tStatic("logic.functions", "USE_RUN_STREAM.THE_STREAM_ENDED_WITH");
};

/**
 * Follows a run's streamed answer live (F-5) while `live` holds — the run has started its handler.
 * Each piece is added as it arrives; screen updates are batched to one per frame, so a fast stream
 * does not re-render per piece.
 *
 * Opened once per run. If the run ends having streamed nothing, the request is dropped instead of
 * held until the server's own time limit. A stream that already started is read to its end even
 * after the run is reported finished: its last pieces are still on the way.
 */
export const useRunStream = (runId: string | null | undefined, live: boolean): IRunStream => {
  const [stream, setStream] = useState<IRunStream>(IDLE);
  const [shownFor, setShownFor] = useState(runId);
  const controller = useRef<AbortController | null>(null);
  const received = useRef(false);
  const openedFor = useRef<string | null>(null);

  // A new run starts from nothing. Set during render (React's "adjust state when inputs change").
  if (shownFor !== runId) {
    setShownFor(runId);
    setStream(IDLE);
  }

  // The old run's request is dropped when the run changes, or the panel goes away.
  useEffect(() => {
    received.current = false;
    openedFor.current = null;
    return () => {
      controller.current?.abort();
      controller.current = null;
    };
  }, [runId]);

  useEffect(() => {
    if (!runId || !live || openedFor.current === runId) return;
    openedFor.current = runId;
    const abort = new AbortController();
    controller.current = abort;

    let text = "";
    let clipped = false;
    let frame: number | null = null;
    const flush = () => {
      frame = null;
      setStream((current) => ({ ...current, text, clipped }));
    };

    const read = async () => {
      try {
        const body = await functionService.streamRun(runId, abort.signal);
        for await (const event of readSse(body)) {
          if (event.event === "message") {
            if (!received.current) {
              received.current = true;
              setStream((current) => ({ ...current, state: "streaming" }));
            }
            if (text.length < RUN_STREAM_MAX_CHARS) {
              text += event.data;
              if (text.length > RUN_STREAM_MAX_CHARS) {
                text = text.slice(0, RUN_STREAM_MAX_CHARS);
                clipped = true;
              }
            } else {
              clipped = true;
            }
            frame ??= requestAnimationFrame(flush);
          } else if (event.event === "done" || event.event === "error") {
            if (frame !== null) cancelAnimationFrame(frame);
            const error = event.event === "error" ? errorMessageOf(event.data) : undefined;
            setStream({ text, clipped, state: event.event, error });
            return;
          }
        }
      } catch {
        // Dropped on purpose, or the connection failed: the run's own record still has the
        // result and logs, which the panel shows when the run ends.
      }
      if (frame !== null) cancelAnimationFrame(frame);
      if (!abort.signal.aborted && received.current) {
        setStream((current) => ({ ...current, text, clipped, state: "done" }));
      }
    };
    void read();
  }, [runId, live]);

  // The run ended and nothing came: a handler that does not stream. Stop waiting for one.
  useEffect(() => {
    if (live || received.current) return;
    controller.current?.abort();
  }, [live]);

  return stream;
};
