/** Starter `index.js` for a newly created function — mirrors the Runner's `bootstrap.mjs` contract (`export default async function handler(input, ctx)`). */
export const STARTER_INDEX_JS = `/**
 * @param {unknown} input - the JSON body the function was invoked with
 * @param {{ context: object, env: Record<string, string>, run: object, log: { debug: Function, info: Function, warn: Function, error: Function } }} ctx
 */
export default async function handler(input, ctx) {
  ctx.log.info("received input", input);

  return {
    message: "Hello from your function!",
    input,
  };
}
`;

export const STARTER_PACKAGE_JSON = `{
  "name": "function",
  "version": "1.0.0",
  "private": true,
  "type": "module",
  "dependencies": {}
}
`;
