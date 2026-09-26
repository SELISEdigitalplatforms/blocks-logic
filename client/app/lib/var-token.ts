/**
 * Configuration-variable token: the literal `{{$VAR.` prefix, a name in `[A-Za-z0-9._:-]`, then
 * `}}`. Mirrors the server regex in `Proxy.DomainService/Utils/ProxyVarRef.cs` — case-sensitive,
 * so `{{ $VAR.x }}` / `{{$var.x}}` / `${SECRET.X}` are literal text, not tokens. The same token is
 * resolved in proxy config and in every workflow node parameter.
 */
export const VAR_REF_RE = /\{\{\$VAR\.[A-Za-z0-9._:-]+\}\}/;

/** True when `value` contains at least one `{{$VAR.name}}` token. */
export const containsVarRef = (value: string) => VAR_REF_RE.test(value);

/** The token literal for a variable name, e.g. `buildVarToken("api-key") === "{{$VAR.api-key}}"`. */
export const buildVarToken = (name: string) => `{{$VAR.${name}}}`;
