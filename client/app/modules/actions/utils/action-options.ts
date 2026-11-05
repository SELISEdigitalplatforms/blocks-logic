import { IActionOption } from "../types/actions.type";

/** What a picker shows for one action: `{label} ({name})`. */
export const actionOptionLabel = (action: IActionOption): string => `${action.label} (${action.name})`;

/** The fields of the action's `data`, e.g. `userId (string), roles (string[])`; empty when it has none. */
export const payloadHint = (action: IActionOption | undefined): string =>
  Object.entries(action?.payload ?? {})
    .map(([field, type]) => `${field} (${type})`)
    .join(", ");

/** Case-insensitive match on the label, the name and the service. An empty search matches all. */
export const matchesAction = (action: IActionOption, search: string): boolean => {
  const query = search.trim().toLowerCase();
  if (query === "") return true;
  return [action.label, action.name, action.service].some((text) => text.toLowerCase().includes(query));
};
