export const BREADCRUMB_CUSTOM_TITLES: Record<string, string | null> = {};

/**
 * Records a custom breadcrumb title for `path`. The breadcrumb reads the map on its next
 * render; pages call this while rendering so the title tracks the page's current state.
 */
export function setBreadcrumbTitle(path: string, title: string | null) {
  BREADCRUMB_CUSTOM_TITLES[path] = title;
}
