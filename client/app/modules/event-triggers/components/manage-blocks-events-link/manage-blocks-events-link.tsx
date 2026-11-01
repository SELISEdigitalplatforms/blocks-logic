import { useScopedPath } from "@seliseblocks/genesis-os";
import { ExternalLink } from "lucide-react";
import { EVENT_TRIGGERS_PATH } from "../../constants/endpoint.constant";

/**
 * Opens the project's Event Triggers page in a new tab, so the workflow editor and its unsaved draft stay as
 * they are. A component (not schema data) because the scoped URL comes from a hook.
 */
export const ManageBlocksEventsLink = () => {
  const scoped = useScopedPath();
  return (
    <a
      href={scoped(EVENT_TRIGGERS_PATH)}
      target="_blank"
      rel="noopener noreferrer"
      aria-label="Manage events (opens in a new tab)"
      className="inline-flex items-center gap-1 font-medium text-primary hover:underline"
    >
      Manage events
      <ExternalLink aria-hidden="true" size={14} />
    </a>
  );
};
