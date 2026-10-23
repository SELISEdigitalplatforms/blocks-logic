import { describe, expect, it } from "vitest";
import type { IEventCatalogItem } from "../types/event.types";
import {
  MAX_TRIGGER_EVENTS,
  addTriggerEvent,
  addableCatalog,
  buildEventSnippet,
  eventTitle,
  isPublicHttpTrigger,
  isValidEventName,
  mapSaveErrors,
  mergeSettings,
  parsePayloadJson,
  triggeredByLabel,
  validateEventPayload,
} from "./events";

const ORDER_PAID = "order.paid";

const CATALOG: IEventCatalogItem[] = [
  { type: "SendMail", title: "Send mail", description: "", publicAllowed: false, payloadTemplate: { to: "" } },
  { type: "FunctionEvent", title: "Function event", description: "", publicAllowed: true, payloadTemplate: { name: "" } },
];

describe("isValidEventName", () => {
  it.each(["abc", ORDER_PAID, "a_b-c.9", "a".repeat(64)])("accepts %s", (name) => {
    expect(isValidEventName(name)).toBe(true);
  });

  it.each(["ab", "Order", "1abc", "a b c", "", "a".repeat(65), "abc!"])("refuses %j", (name) => {
    expect(isValidEventName(name)).toBe(false);
  });
});

describe("parsePayloadJson", () => {
  it("returns the object", () => {
    expect(parsePayloadJson('{"a":1}')).toEqual({ ok: true, value: { a: 1 } });
  });

  it("refuses broken JSON, empty text, arrays, null and plain values", () => {
    for (const text of ["{", "", "   ", "[]", "null", "5", '"x"']) {
      expect(parsePayloadJson(text).ok).toBe(false);
    }
  });

  it("says why", () => {
    const result = parsePayloadJson("{");
    expect(result.ok === false && result.error).toContain("Not valid JSON");
  });
});

describe("validateEventPayload", () => {
  it("only FunctionEvent has a name rule", () => {
    expect(validateEventPayload("SendMail", {})).toBeNull();
    expect(validateEventPayload("FunctionEvent", {})).toContain("name");
    expect(validateEventPayload("FunctionEvent", { name: "Bad Name" })).toContain("not valid");
    expect(validateEventPayload("FunctionEvent", { name: ORDER_PAID })).toBeNull();
  });
});

describe("addableCatalog", () => {
  it("hides types already added", () => {
    const added = [{ type: "SendMail" as const, payload: {} }];
    expect(addableCatalog(CATALOG, added, false).map((i) => i.type)).toEqual(["FunctionEvent"]);
  });

  it("keeps the type being edited", () => {
    const added = [{ type: "SendMail" as const, payload: {} }];
    expect(addableCatalog(CATALOG, added, false, "SendMail")).toHaveLength(2);
  });

  it("offers only publicAllowed types to a public function", () => {
    expect(addableCatalog(CATALOG, [], true).map((i) => i.type)).toEqual(["FunctionEvent"]);
    expect(addableCatalog(CATALOG, [], false)).toHaveLength(2);
  });
});

describe("isPublicHttpTrigger", () => {
  it("is public only with HTTP on and Public auth", () => {
    expect(isPublicHttpTrigger({ httpEnabled: true, authMode: "Public" })).toBe(true);
    expect(isPublicHttpTrigger({ httpEnabled: true, authMode: "Token" })).toBe(false);
    expect(isPublicHttpTrigger({ httpEnabled: false, authMode: "Public" })).toBe(false);
  });
});

describe("buildEventSnippet", () => {
  it("builds the ctx.events.add line", () => {
    expect(buildEventSnippet({ type: "FunctionEvent", payload: { name: ORDER_PAID } })).toBe(
      "ctx.events.add('FunctionEvent', {\n  \"name\": \"order.paid\"\n});",
    );
  });
});

describe("eventTitle", () => {
  it("uses the catalog title, else the type", () => {
    expect(eventTitle({ type: "SendMail", payload: {} }, CATALOG)).toBe("Send mail");
    expect(eventTitle({ type: "SendMail", payload: {} }, [])).toBe("SendMail");
  });
});

describe("mergeSettings", () => {
  const latest = { events: [{ type: "SendMail" as const, payload: { to: "a" } }], triggerEvents: [ORDER_PAID] };

  it("a events patch keeps the latest trigger events", () => {
    expect(mergeSettings(latest, "fn_1", { events: [] })).toEqual({
      functionId: "fn_1",
      events: [],
      triggerEvents: [ORDER_PAID],
    });
  });

  it("a trigger patch keeps the latest events", () => {
    expect(mergeSettings(latest, "fn_1", { triggerEvents: [] })).toEqual({
      functionId: "fn_1",
      events: latest.events,
      triggerEvents: [],
    });
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
  it("puts each message on its event row, trigger list or general", () => {
    const mapped = mapSaveErrors({
      invalid_request:
        'events[0].payload: unknown field "x"; events[2].type: not allowed; triggerEvents[1]: bad name',
      other: "something else",
    });
    expect(mapped.byEvent).toEqual({ 0: ['unknown field "x"'], 2: ["not allowed"] });
    expect(mapped.trigger).toEqual(["triggerEvents[1]: bad name"]);
    expect(mapped.general).toEqual(["something else"]);
  });

  it("a message that contains '; ' stays with its path", () => {
    const mapped = mapSaveErrors({ invalid_request: "events[1].payload: a; b" });
    expect(mapped.byEvent).toEqual({ 1: ["a; b"] });
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
