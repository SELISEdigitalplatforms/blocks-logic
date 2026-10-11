import { SecretListItem } from "@/models/secret";

/**
 * How one module writes a reference to a platform configuration variable.
 *
 * Two formats exist and neither can move: Proxy's `{{$VAR.name}}` is resolved by name in
 * `ProxyVariableResolver`, and Functions' `{{secret.<id>}}` is resolved by id in
 * `OutputActionProcessor` and `FunctionEnvelopeBuilder`. Everything above this file is written
 * against the codec rather than against either literal, so a picker, a chip or a token field can
 * be dropped into a third module (Workflow) by naming the codec it should speak.
 */
export type VariableRefCodec = {
  /** The token to store for a chosen variable. */
  build: (variable: SecretListItem) => string;
  /**
   * A fresh global regex capturing the lookup key inside a token. Built per call on purpose —
   * a shared global regex carries `lastIndex` between calls and silently skips matches.
   */
  pattern: () => RegExp;
  /** The catalog row a captured key refers to, or undefined when nothing matches. */
  find: (key: string, catalog: SecretListItem[]) => SecretListItem | undefined;
  /**
   * Whether the key inside a token is meaningless to a person and must never be shown.
   *
   * True for an id: it identifies a credential, it is nothing anyone can act on, and putting one
   * on screen only invites it into screenshots and support threads. A field holding these shows
   * names instead and translates back on every change. False for a name, which is already what
   * the author typed — rewriting it would hide nothing and change a syntax people know.
   */
  opaqueKey: boolean;
};

/** Functions: `{{secret.<id>}}`. The id survives a rename in the catalog; the name does not. */
export const secretIdRef: VariableRefCodec = {
  build: (variable) => `{{secret.${variable.id}}}`,
  pattern: () => /\{\{secret\.([\w-]+)\}\}/g,
  find: (key, catalog) => catalog.find((variable) => variable.id === key),
  opaqueKey: true,
};

/** Proxy: `{{$VAR.name}}`. Mirrors `VAR_REF_RE` in the proxy utils, which the server agrees with. */
export const varNameRef: VariableRefCodec = {
  build: (variable) => `{{$VAR.${variable.name}}}`,
  pattern: () => /\{\{\$VAR\.([A-Za-z0-9._:-]+)\}\}/g,
  find: (key, catalog) => catalog.find((variable) => variable.name === key),
  opaqueKey: false,
};

/**
 * The key referenced when `value` is exactly one token and nothing else, otherwise null.
 *
 * That distinction is what the Variables editor is built on: a value that *is* a reference can be
 * shown as a chip, while one that merely contains a reference (`Bearer {{secret.x}}`) cannot —
 * there is no chip that would honestly represent the surrounding text.
 */
export const soleRefKey = (value: string, codec: VariableRefCodec): string | null => {
  const match = value.trim().match(new RegExp(`^${codec.pattern().source}$`));
  return match ? match[1] : null;
};

/** Every key referenced in `value`, in order, including duplicates. */
export const refKeys = (value: string, codec: VariableRefCodec): string[] =>
  [...value.matchAll(codec.pattern())].map((match) => match[1]);

/** Whether `value` references at least one variable, embedded or not. */
export const hasRef = (value: string, codec: VariableRefCodec) => codec.pattern().test(value);

/**
 * `value` with every token's key swapped for the variable's name, for display only.
 *
 * A stored Functions token carries an opaque id, which must never be shown: an id is not
 * something a person can act on, and putting one on screen invites it into screenshots, tickets
 * and support threads. For a name-keyed codec this is the identity function, which is what lets
 * one field component serve both modules.
 */
export const toDisplayValue = (
  value: string,
  codec: VariableRefCodec,
  catalog: SecretListItem[],
): string => {
  if (!codec.opaqueKey) return value;
  return value.replace(codec.pattern(), (token, key: string) => {
    const variable = codec.find(key, catalog);
    return variable ? displayToken(variable) : token;
  });
};

/**
 * The token to type into a field for a chosen variable: the stored token itself when the key is
 * already a name, and the display form when it is an opaque id.
 */
export const displayToken = (variable: SecretListItem, codec?: VariableRefCodec) =>
  codec && !codec.opaqueKey ? codec.build(variable) : `{{variable.${variable.name}}}`;

/** The inverse of {@link toDisplayValue}: display names back to the keys that get stored. */
export const fromDisplayValue = (
  display: string,
  codec: VariableRefCodec,
  catalog: SecretListItem[],
): string => {
  if (!codec.opaqueKey) return display;
  return display.replace(/\{\{variable\.([^}]+)\}\}/g, (token, name: string) => {
    const variable = catalog.find((each) => each.name === name);
    return variable ? codec.build(variable) : token;
  });
};

/**
 * Keys in `value` that the catalog cannot name — a variable deleted or renamed since, or simply
 * a catalog that has not loaded.
 *
 * A field holding one of these cannot be safely edited as display text: there is no name to show
 * in place of the key, and round-tripping the edit would either expose the key or drop it. The
 * caller's answer is to lock the field and offer to clear it, not to render the key.
 */
export const unresolvableRefKeys = (
  value: string,
  codec: VariableRefCodec,
  catalog: SecretListItem[],
): string[] => refKeys(value, codec).filter((key) => !codec.find(key, catalog));
