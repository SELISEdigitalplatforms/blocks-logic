/**
 * The save rule for typed credentials, mirrored from the host so the editors can say it before Save does.
 * Host: `FunctionSecretLiterals` (server) with `ProxySecretRedactor.IsSensitiveKey` for the name test.
 *
 * A variable or output-action header whose name looks like a credential must hold a
 * `{{secret.<id>}}` reference (a `Bearer` / `Basic` / `Token` word beside it is fine). Save refuses a
 * new or changed typed value there; a value already stored unchanged still saves.
 */

// Same words as ProxySecretRedactor.SensitiveKeyPattern, case-insensitive.
const CREDENTIAL_NAME =
  /(key|token|secret|passw|pwd|signature|^sig$|auth|session|credential|cookie|bearer|jwt)/i;
const SECRET_REF = /\{\{secret\.[\w-]+\}\}/g;
const SCHEME_WORDS = ["bearer", "basic", "token"];

/** True when `name` looks like it holds a credential. */
export const isCredentialName = (name: string | null | undefined): boolean =>
  !!name && CREDENTIAL_NAME.test(name);

/** True when `value` has typed text besides `{{secret.<id>}}` references and an auth scheme word. */
export const hasTypedValue = (value: string | null | undefined): boolean => {
  if (!value || !value.trim()) return false;
  const rest = value.replace(SECRET_REF, "").trim();
  if (!rest) return false;
  return !SCHEME_WORDS.includes(rest.toLowerCase());
};

/** True when saving this name/value pair is refused, unless the same pair is already stored. */
export const isTypedCredential = (
  name: string | null | undefined,
  value: string | null | undefined,
) => isCredentialName(name) && hasTypedValue(value);

/** The words both editors show under such a row. True for new values and for unchanged stored ones. */
export const TYPED_CREDENTIAL_MESSAGE =
  "This name looks like a credential. Save refuses a new or changed typed value here; use a configuration variable instead.";
