using Functions.DomainService.Models;

namespace Functions.DomainService.Utils
{
    /// <summary>
    /// The three starters the create dialog offers (FEATURES-AND-UI §4.2). An unknown or missing
    /// name gives the minimal handler, so a client that sends nothing keeps working.
    /// </summary>
    public static class FunctionStarterTemplates
    {
        public const string Minimal = "Minimal";
        public const string HttpEcho = "HttpEcho";
        public const string FetchTransform = "FetchTransform";

        public static FunctionSource For(string? template) => new()
        {
            IndexJs = template switch
            {
                HttpEcho => HttpEchoIndexJs,
                FetchTransform => FetchTransformIndexJs,
                _ => MinimalIndexJs,
            },
            PackageJson = PackageJson,
        };

        // `input` is the request for an HTTP or Test run — { method, path, query, headers, body } —
        // and the previous node's output for a workflow run. The starters read input.body so they
        // behave the same from the editor's Run test and from the public URL.
        private const string MinimalIndexJs = """
            /**
             * @param {FunctionInput} input - the request: method, path, query, headers and body
             * @param {FunctionContext} ctx - env, run, caller context and the logger
             */
            export default async function handler(input, ctx) {
              ctx.log.info("Function invoked", { method: input.method, path: input.path });
              return { received: input.body };
            }
            """;

        private const string HttpEchoIndexJs = """
            /**
             * @param {FunctionInput} input - the request: method, path, query, headers and body
             * @param {FunctionContext} ctx - env, run, caller context and the logger
             */
            export default async function handler(input, ctx) {
              ctx.log.info("Invoked", { invokedBy: ctx.run.invokedBy, method: input.method });

              // Anything after /fn/{id} is yours to route on: GET .../orders/42 → "orders/42".
              return {
                method: input.method,
                path: input.path,
                query: input.query,
                body: input.body,
                caller: {
                  userId: ctx.context.userId,
                  isAuthenticated: ctx.context.isAuthenticated,
                },
              };
            }
            """;

        private const string FetchTransformIndexJs = """
            /**
             * @param {FunctionInput} input - the request: method, path, query, headers and body
             * @param {FunctionContext} ctx - env, run, caller context and the logger
             */
            export default async function handler(input, ctx) {
              // Variables arrive as plain strings on ctx.env; set them under Configuration.
              const base = ctx.env.API_BASE ?? "https://api.exchangerate.host";

              const response = await fetch(`${base}/latest?base=USD`);
              if (!response.ok) {
                throw new Error(`upstream responded ${response.status}`);
              }

              const payload = await response.json();
              ctx.log.info("fetched rates", { count: Object.keys(payload.rates ?? {}).length });

              return { base: payload.base, eur: payload.rates?.EUR ?? null, requested: input.body };
            }
            """;

        private const string PackageJson = """
            {
              "name": "function",
              "version": "1.0.0",
              "private": true,
              "type": "module",
              "main": "index.js"
            }
            """;
    }
}
