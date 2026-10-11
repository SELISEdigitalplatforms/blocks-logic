/**
 * A stored run's input is the whole request — `{ method, path, query, headers, body }` — because
 * that is what the handler received. The test box holds only the payload (Test wraps it the same
 * way server-side: the body of a POST, the query of a GET), so reusing a run's input means lifting
 * that part back out. Anything not in that shape (an older run, a workflow input) is reused as is.
 */
export const payloadOf = (storedInput: string): string => {
  try {
    const parsed: unknown = JSON.parse(storedInput);
    if (
      parsed &&
      typeof parsed === "object" &&
      !Array.isArray(parsed) &&
      typeof (parsed as { method?: unknown }).method === "string" &&
      "body" in parsed
    ) {
      const request = parsed as { method: string; body: unknown; query?: unknown };
      const payload = request.method === "GET" ? request.query : request.body;
      return payload == null ? "{}" : JSON.stringify(payload, null, 2);
    }
  } catch {
    // Not JSON: hand it back untouched.
  }
  return storedInput;
};
