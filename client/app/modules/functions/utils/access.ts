import { ITriggerConfig, MatchMode } from "../types/function.types";

const listClause = (noun: string, values: string[], match: MatchMode) => {
  if (values.length === 1) return `the ${noun} ${values[0]}`;
  const quantifier = match === "All" ? "all of the" : "any of the";
  return `${quantifier} ${noun}s ${values.join(", ")}`;
};

/**
 * The footer sentence under "Restrict further" — one place so the card, the detail header and the
 * tests agree on what a trigger means. Word for word the proxy's `describeProxyAccess`, over the
 * functions trigger shape, so a tenant reads the same sentence for the same policy on both pages.
 */
export const describeTriggerAccess = (trigger: ITriggerConfig): string => {
  if (trigger.authMode === "Public") {
    return "Anyone with the URL and your project key can call it. No identity, no token-scoped work.";
  }

  const hasRoles = trigger.roles.length > 0;
  const hasPermissions = trigger.permissions.length > 0;
  if (!hasRoles && !hasPermissions) {
    return "No extra restriction — any signed-in caller with a valid Blocks token can invoke it.";
  }

  const roles = hasRoles ? listClause("role", trigger.roles, trigger.roleMatch) : null;
  const permissions = hasPermissions
    ? listClause("permission", trigger.permissions, trigger.permissionMatch)
    : null;
  if (roles && permissions) {
    return `Callers must hold ${roles} ${trigger.combine === "And" ? "AND" : "OR"} ${permissions}.`;
  }
  return `Callers must hold ${roles ?? permissions}.`;
};
