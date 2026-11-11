/**
 * Paging and search rules shared by the inspector's remote-search pickers
 * (`conditional-multiselect` and the `json-fields` list picker).
 */

/** Options requested per page. */
export const REMOTE_SEARCH_PAGE_SIZE = 20;

/** Typing pause before the trimmed search text is sent. */
export const REMOTE_SEARCH_DEBOUNCE_MS = 500;

/** The next page loads once the list is scrolled this close to its bottom. */
export const REMOTE_SEARCH_SCROLL_THRESHOLD_PX = 32;
