/** Logic page (relative to the project scope) that opens the project's Actions page in Blocks OS. */
export const OPEN_IN_OS_ACTIONS_PATH = "open-in-os/actions";

/** The Blocks OS page it lands on, relative to the same project scope. Fixed: never taken from input. */
export const OS_ACTIONS_PATH = "secret-management/actions";

const PROJECT_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** A project id is a GUID; anything else is refused before IAM is asked. */
export const isProjectId = (value: string | undefined): value is string => !!value && PROJECT_ID.test(value);

/** An unset runtime value, or one the deployment left as its `__NAME__` placeholder. */
export const isUnset = (value: string | undefined): boolean => !value || /^__.*__$/.test(value);
