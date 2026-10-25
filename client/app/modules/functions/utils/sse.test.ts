import { describe, expect, it } from "vitest";
import { readSse, type ISseEvent } from "./sse";

const bodyOf = (...reads: (string | Uint8Array)[]) =>
  new ReadableStream<Uint8Array>({
    start(controller) {
      const encoder = new TextEncoder();
      for (const read of reads) {
        controller.enqueue(typeof read === "string" ? encoder.encode(read) : read);
      }
      controller.close();
    },
  });

const all = async (body: ReadableStream<Uint8Array>) => {
  const events: ISseEvent[] = [];
  for await (const event of readSse(body)) events.push(event);
  return events;
};

describe("readSse", () => {
  it("reads data events, then the named end event", async () => {
    expect(await all(bodyOf("data: Hel\n\ndata: lo\n\n", "event: done\ndata: {}\n\n"))).toEqual([
      { event: "message", data: "Hel" },
      { event: "message", data: "lo" },
      { event: "done", data: "{}" },
    ]);
  });

  it("joins the data lines of one event and keeps a piece's own newlines", async () => {
    expect(await all(bodyOf("data: line 1\ndata: line 2\n\n"))).toEqual([
      { event: "message", data: "line 1\nline 2" },
    ]);
  });

  it("waits for a line split across reads, and handles \\r\\n", async () => {
    expect(await all(bodyOf("da", "ta: a", "b\r\n\r", "\n"))).toEqual([{ event: "message", data: "ab" }]);
  });

  it("keeps a character split across two reads", async () => {
    const bytes = new TextEncoder().encode("data: café\n\n");
    const cut = bytes.indexOf(0xc3) + 1;
    expect(await all(bodyOf(bytes.slice(0, cut), bytes.slice(cut)))).toEqual([
      { event: "message", data: "café" },
    ]);
  });

  it("drops comments and an event the body ends inside", async () => {
    expect(await all(bodyOf(": keep-alive\n\ndata: whole\n\ndata: cut off"))).toEqual([
      { event: "message", data: "whole" },
    ]);
  });

  it("reads an error event's JSON as it was sent", async () => {
    const error = '{"runId":"r1","status":"FAILED","errorCode":"USER_ERROR","message":"boom"}';
    expect(await all(bodyOf(`event: error\ndata: ${error}\n\n`))).toEqual([{ event: "error", data: error }]);
  });
});
