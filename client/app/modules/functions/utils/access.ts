import { ITriggerConfig, MatchMode } from "../types/function.types";
import { tStatic } from "@seliseblocks/genesis-os";

const LOGIC_FUNCTIONS_MODULE = "logic.functions";

const listClause = (kind: "role" | "permission", values: string[], match: MatchMode) => {
  const list = values.join(", ");
  if (values.length === 1) {
    return kind === "role"
      ? tStatic(LOGIC_FUNCTIONS_MODULE, "ACCESS.LIST_ONE_ROLE", { value: list })
      : tStatic(LOGIC_FUNCTIONS_MODULE, "ACCESS.LIST_ONE_PERMISSION", { value: list });
  }
  if (kind === "role") {
    return match === "All"
      ? tStatic(LOGIC_FUNCTIONS_MODULE, "ACCESS.LIST_ALL_ROLES", { values: list })
      : tStatic(LOGIC_FUNCTIONS_MODULE, "ACCESS.LIST_ANY_ROLES", { values: list });
  }
  return match === "All"
    ? tStatic(LOGIC_FUNCTIONS_MODULE, "ACCESS.LIST_ALL_PERMISSIONS", { values: list })
    : tStatic(LOGIC_FUNCTIONS_MODULE, "ACCESS.LIST_ANY_PERMISSIONS", { values: list });
};

/**
 * The footer sentence under "Restrict further" — one place so the card, the detail header and the
 * tests agree on what a trigger means. Word for word the proxy's `describeProxyAccess`, over the
 * functions trigger shape, so a tenant reads the same sentence for the same policy on both pages.
 */
export const describeTriggerAccess = (trigger: ITriggerConfig): string => {
  if (trigger.authMode === "Public") {
    return tStatic(LOGIC_FUNCTIONS_MODULE, "ACCESS.ANYONE_WITH_THE_URL");
  }

  const hasRoles = trigger.roles.length > 0;
  const hasPermissions = trigger.permissions.length > 0;
  if (!hasRoles && !hasPermissions) {
    return tStatic(LOGIC_FUNCTIONS_MODULE, "ACCESS.NO_EXTRA_RESTRICTION_ANY");
  }

  const roles = hasRoles ? listClause("role", trigger.roles, trigger.roleMatch) : null;
  const permissions = hasPermissions
    ? listClause("permission", trigger.permissions, trigger.permissionMatch)
    : null;
  if (roles && permissions) {
    return tStatic(
      LOGIC_FUNCTIONS_MODULE,
      trigger.combine === "And" ? "ACCESS.MUST_HOLD_BOTH_AND" : "ACCESS.MUST_HOLD_BOTH_OR",
      { roles, permissions },
    );
  }
  return tStatic(LOGIC_FUNCTIONS_MODULE, "ACCESS.MUST_HOLD", { clause: roles ?? permissions ?? "" });
};
