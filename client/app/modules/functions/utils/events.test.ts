import { describe, expect, it } from "vitest";
import {
  MAX_TRIGGER_EVENTS,
  addTriggerEvent,
  isValidEventName,
  mapSaveErrors,
  triggeredByLabel,
} from "./events";

const ORDER_PAID = "order.paid";

describe("isValidEventName", () => {
  it.each(["abc", ORDER_PAID, "a_b-c.9", "a".repeat(64)])("accepts %s", (name) => {
    expect(isValidEventName(name)).toBe(true);
  });

  it.each(["ab", "Order", "1abc", "a b c", "", "a".repeat(65), "abc!"])("refuses %j", (name) => {
    expect(isValidEventName(name)).toBe(false);
  });
});

describe("addTriggerEvent", () => {
  it("adds a trimmed valid name", () => {
    expect(addTriggerEvent(["abc"], "  order.paid ")).toEqual({ ok: true, list: ["abc", ORDER_PAID] });
  });

  it("refuses an invalid name, a duplicate and the 21st", () => {
    expect(addTriggerEvent([], "Bad").ok).toBe(false);
    expect(addTriggerEvent(["abc"], "abc")).toEqual({ ok: false, error: "This event is already in the list." });
    const full = Array.from({ length: MAX_TRIGGER_EVENTS }, (_, i) => `event${i}`);
    expect(addTriggerEvent(full, "another").ok).toBe(false);
  });
});

describe("mapSaveErrors", () => {
  it("puts each message on the trigger list or general", () => {
    const mapped = mapSaveErrors({
      invalid_request: "triggerEvents[1]: bad name; triggerEvents: at most 20 event names",
      other: "something else",
    });
    expect(mapped.trigger).toEqual(["triggerEvents[1]: bad name", "triggerEvents: at most 20 event names"]);
    expect(mapped.general).toEqual(["something else"]);
  });

  it("a message that contains '; ' stays with its path", () => {
    expect(mapSaveErrors({ invalid_request: "triggerEvents[1]: a; b" }).trigger).toEqual(["triggerEvents[1]: a; b"]);
  });

  it("an error without a path is general", () => {
    expect(mapSaveErrors({ forbidden: "No permission" }).general).toEqual(["No permission"]);
  });
});

describe("triggeredByLabel", () => {
  const labels = { Http: "HTTP", Event: "Event" };

  it("names the event for an event run", () => {
    expect(triggeredByLabel("Event", "evt_1", labels)).toBe("event evt_1");
    expect(triggeredByLabel("Event", null, labels)).toBe("Event");
    expect(triggeredByLabel("Http", "x", labels)).toBe("HTTP");
  });
});
