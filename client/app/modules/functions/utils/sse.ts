/** One Server-Sent Event: its name (`message` when the server gave none) and its data lines joined. */
export interface ISseEvent {
  event: string;
  data: string;
}

/**
 * Reads a `text/event-stream` body as events, in order, as they arrive. `EventSource` cannot be
 * used for a run's stream: it only sends GET and cannot carry the console's headers.
 *
 * Follows the SSE rules the server writes to: `data:` lines of one event are joined with `\n`,
 * `event:` names it, a blank line ends it, a `:` line is a comment, and one optional space after
 * the colon is dropped. Bytes are decoded as a stream, so a character split across two reads
 * survives. An event the body ends in the middle of is not delivered.
 */
export async function* readSse(body: ReadableStream<Uint8Array>): AsyncGenerator<ISseEvent> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let event = "";
  let data: string[] = [];

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) return;
      buffer += decoder.decode(value, { stream: true });

      let newline = buffer.indexOf("\n");
      while (newline >= 0) {
        const line = buffer.slice(0, newline).replace(/\r$/, "");
        buffer = buffer.slice(newline + 1);
        newline = buffer.indexOf("\n");

        if (line === "") {
          if (data.length > 0) yield { event: event || "message", data: data.join("\n") };
          event = "";
          data = [];
          continue;
        }
        if (line.startsWith(":")) continue;

        const colon = line.indexOf(":");
        const field = colon < 0 ? line : line.slice(0, colon);
        const raw = colon < 0 ? "" : line.slice(colon + 1);
        const fieldValue = raw.startsWith(" ") ? raw.slice(1) : raw;
        if (field === "data") data.push(fieldValue);
        else if (field === "event") event = fieldValue;
      }
    }
  } finally {
    reader.releaseLock();
  }
}
