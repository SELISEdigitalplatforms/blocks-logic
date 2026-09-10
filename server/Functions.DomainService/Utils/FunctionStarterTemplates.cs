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

        private const string MinimalIndexJs = """
            /**
             * @param {unknown} input - the JSON body this function was invoked with
             * @param {FunctionContext} ctx - env, run, caller context and the logger
             */
            export default async function handler(input, ctx) {
              ctx.log.info("Function invoked", { input });
              return { received: input };
            }
            """;

        private const string HttpEchoIndexJs = """
            /**
             * @param {unknown} input - the JSON body this function was invoked with
             * @param {FunctionContext} ctx - env, run, caller context and the logger
             */
            export default async function handler(input, ctx) {
              ctx.log.info("Invoked", { invokedBy: ctx.run.invokedBy });

              return {
                received: input,
                caller: {
                  userId: ctx.context.userId,
                  isAuthenticated: ctx.context.isAuthenticated,
                },
              };
            }
            """;

        private const string FetchTransformIndexJs = """
            /**
             * @param {unknown} input - the JSON body this function was invoked with
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

              return { base: payload.base, eur: payload.rates?.EUR ?? null, input };
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
