import { ReactNode } from "react";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui-kits/accordion/accordion";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { SANDBOX_CODE_RULES } from "../../constants/limits.constant";

/**
 * Everything someone writing a function needs, in one place beside the editor. The numbers are the
 * platform's fixed profile (FunctionLimits.Ceiling, protocol.mjs, the sandbox profile and
 * FunctionHttpResponseMapper) — keep them in step when those change.
 */
const Code = ({ children }: { children: ReactNode }) => (
  <code className="font-mono text-[11px]">{children}</code>
);

const Block = ({ children }: { children: string }) => (
  <pre className="overflow-x-auto whitespace-pre rounded-md bg-surface-app p-2 font-mono text-[11px] leading-relaxed text-medium-emphasis">
    {children}
  </pre>
);

const P = ({ children }: { children: ReactNode }) => (
  <p className="text-xs leading-relaxed text-medium-emphasis">{children}</p>
);

const Items = ({ items }: { items: ReactNode[] }) => (
  <ul className="flex list-disc flex-col gap-1 pl-4">
    {items.map((item, index) => (
      <li key={index} className="text-xs leading-relaxed text-medium-emphasis">
        {item}
      </li>
    ))}
  </ul>
);

export const GUIDE_SECTIONS = [
  "handler",
  "speed",
  "rules",
  "api",
  "webhooks",
  "streaming",
  "limits",
  "network",
  "packages",
  "debugging",
] as const;

export const FunctionGuide = () => (
  <Card>
    <CardContent className="p-2">
      <Accordion type="multiple" defaultValue={["handler", "speed"]} data-testid="function-guide">
        <AccordionItem value="handler">
          <AccordionTrigger className="px-2 text-sm">The handler</AccordionTrigger>
          <AccordionContent className="flex flex-col gap-2 px-2">
            <Block>{`export default async function handler(input, ctx) {
  return { ok: true }; // becomes the run's result
}`}</Block>
            <Items
              items={[
                <>
                  <Code>input</Code>: <Code>method</Code>, <Code>path</Code>, <Code>query</Code>,{" "}
                  <Code>headers</Code>, <Code>body</Code>, <Code>rawBody</Code> (a workflow step
                  passes the previous step&apos;s output instead).
                </>,
                <>
                  <Code>ctx</Code>: <Code>env</Code> (your variables), <Code>context</Code> (the
                  caller), <Code>await blocks.getAccessToken()</Code> (call Blocks as the
                  caller),{" "}
                  <Code>log</Code>, <Code>run</Code>, <Code>waitUntil()</Code>.
                </>,
                <>
                  <Code>input</Code> and <Code>ctx</Code> exist only inside the handler. Code at
                  the top of the file runs once, when the sandbox starts.
                </>,
                <>
                  Get the caller&apos;s token with{" "}
                  <Code>const token = await ctx.blocks.getAccessToken()</Code>. In a warm sandbox
                  it is fetched only when your code asks, so ask on the path that calls Blocks,
                  not at the top of every call. It is asked for once per call; asking again in the same call returns
                  the same token. Never keep it outside the call (a module variable, a listener).{" "}
                  <Code>ctx.blocks.accessToken</Code> was removed.
                </>,
              ]}
            />
          </AccordionContent>
        </AccordionItem>

        <AccordionItem value="speed">
          <AccordionTrigger className="px-2 text-sm">Speed: the sandbox is reused</AccordionTrigger>
          <AccordionContent className="flex flex-col gap-2 px-2">
            <Items
              items={[
                "Calls of a deployed function (HTTP and workflow steps) reuse a warm sandbox when the runner host has reuse on: your module stays loaded and open connections stay open. A warm call costs your handler's own time.",
                "Create database and HTTP clients once, at module level. Never connect and close per call.",
                "With reuse on, every deploy prepares a warm sandbox, so the first call after it is fast too.",
                "Test runs always start a fresh sandbox — don't judge speed by Test.",
                "A sandbox that sits idle, gets old, serves many calls or comes close to its memory limit is replaced; the next call starts cold.",
              ]}
            />
            <Block>{`let client; // module level: reused by every call
export default async function handler(input, ctx) {
  client ??= await connect(ctx.env.DB_URL);
  return client.find(input.body); // request data stays in here
}`}</Block>
          </AccordionContent>
        </AccordionItem>

        <AccordionItem value="rules">
          <AccordionTrigger className="px-2 text-sm">Rules that keep it correct</AccordionTrigger>
          <AccordionContent className="flex flex-col gap-2 px-2">
            <P>
              The next call in the sandbox may be another user&apos;s. Breaking a rule replaces the
              sandbox after the call (the run says why).
            </P>
            <Items items={SANDBOX_CODE_RULES.map((rule) => `Don't ${rule.dont.charAt(0).toLowerCase()}${rule.dont.slice(1)} ${rule.fix}`)} />
          </AccordionContent>
        </AccordionItem>

        <AccordionItem value="api">
          <AccordionTrigger className="px-2 text-sm">Answering as an API</AccordionTrigger>
          <AccordionContent className="flex flex-col gap-2 px-2">
            <P>
              With Response = <strong>Wait for answer</strong> (Trigger tab), the caller gets your
              result directly. Return this shape to control the HTTP answer:
            </P>
            <Block>{`return {
  statusCode: 201,
  headers: { "content-type": "application/json" },
  body: { id: order.id },
};`}</Block>
            <Items
              items={[
                "Anything else you return is sent as 200 + JSON.",
                "Headers that pass: content-type, content-language, content-disposition, cache-control, expires, last-modified, etag, vary, retry-after; location on a 3xx; x-* except x-forwarded-*, x-real-ip, x-original-*, x-blocks-*. The number and size of headers are capped. set-cookie is always dropped.",
                "A failed run answers 502, a timed-out run 504. A call that takes too long gets 202 + a poll token — the run still finishes.",
                "Accepted methods (GET, POST, PUT, PATCH, DELETE) are set on the Trigger tab; any other gets 405.",
              ]}
            />
          </AccordionContent>
        </AccordionItem>

        <AccordionItem value="webhooks">
          <AccordionTrigger className="px-2 text-sm">Receiving webhooks</AccordionTrigger>
          <AccordionContent className="flex flex-col gap-2 px-2">
            <P>
              Verify a webhook with <Code>input.rawBody</Code> (the exact bytes, base64) — never
              with <Code>input.body</Code>, which is parsed and no longer matches the signature.
              Keep the signing secret in a secret-bound variable.
            </P>
            <Block>{`import Stripe from "stripe";
const stripe = new Stripe("unused"); // module level

export default async function handler(input, ctx) {
  const event = stripe.webhooks.constructEvent(
    Buffer.from(input.rawBody, "base64"),
    input.headers["stripe-signature"],
    ctx.env.STRIPE_WEBHOOK_SECRET,
  ); // throws if the signature is wrong
  return { received: event.type };
}`}</Block>
            <Items
              items={[
                "Signature headers that reach the function: stripe-signature, x-hub-signature(-256), x-github-event, x-github-delivery, x-shopify-hmac-sha256, x-shopify-topic, x-shopify-shop-domain, x-shopify-webhook-id, x-slack-signature, x-slack-request-timestamp, svix-id/timestamp/signature, webhook-id/timestamp/signature.",
                "rawBody is null when there is no body, or when the body is too large to carry twice.",
                "Set the trigger to Public: the sender has no Blocks login. The signature check is what protects it.",
              ]}
            />
          </AccordionContent>
        </AccordionItem>

        <AccordionItem value="streaming">
          <AccordionTrigger className="px-2 text-sm">Streaming answers (AI)</AccordionTrigger>
          <AccordionContent className="flex flex-col gap-2 px-2">
            <P>
              With Response = <strong>Wait for answer</strong>, yield pieces instead of returning
              once: the caller gets each piece the moment you yield it.
            </P>
            <Block>{`export default async function* handler(input, ctx) {
  const stream = await openai.chat.completions.create({
    model: "gpt-4o-mini", stream: true,
    messages: [{ role: "user", content: input.body.prompt }],
  });
  for await (const part of stream) {
    yield part.choices[0]?.delta?.content ?? "";
  }
}`}</Block>
            <Items
              items={[
                "Yield strings (sent as they are), bytes (sent as UTF-8 text) or objects (sent as one JSON line each). Returning any async iterable works too, such as a fetch response's body.",
                "The caller gets plain text, or Server-Sent Events when it sends Accept: text/event-stream (each piece a data: event, then event: done).",
                "If the run fails after it started streaming, an SSE caller gets event: error; a plain-text answer is cut off, so the client sees it as incomplete.",
                "A streamed answer has a size cap and must finish inside the run's time limit. The run's result keeps only the start of the text.",
                "Test runs and workflow steps get the whole text at the end, not piece by piece.",
              ]}
            />
          </AccordionContent>
        </AccordionItem>

        <AccordionItem value="limits">
          <AccordionTrigger className="px-2 text-sm">Limits</AccordionTrigger>
          <AccordionContent className="px-2">
            <Items
              items={[
                "Each call has a fixed time limit (work passed to ctx.waitUntil included).",
                "Fixed memory per sandbox, which cannot be raised; 0.1 CPU. Node 24.",
                "The request body, the result and the logs of each call are size-limited.",
                "Read-only filesystem except a small /tmp (files can't be executed).",
                "Processes per sandbox and calls of one function at the same time are capped; extra calls queue.",
                "API mode waits a limited time for the answer, then returns 202 + a poll token.",
                "HTTP calls per minute: 600 for a Public function, no limit for one that needs a login — change it under Trigger → Rate limit. Over it, callers get 429. Workflow steps and Test runs are not counted.",
              ]}
            />
          </AccordionContent>
        </AccordionItem>

        <AccordionItem value="network">
          <AccordionTrigger className="px-2 text-sm">Network</AccordionTrigger>
          <AccordionContent className="px-2">
            <Items
              items={[
                "Outbound calls go to public internet addresses only (fetch over HTTPS, hosted databases such as MongoDB Atlas or Redis Cloud). Which other ports are open is set by the host's egress rules — ask your platform team if a connection times out.",
                "Private and internal addresses are blocked: 10.x, 172.16–31.x, 192.168.x, 127.x, 169.254.x, 100.64–127.x and the host itself. A database inside a company network is not reachable from a function.",
                "Use a public endpoint for anything the function must reach.",
              ]}
            />
          </AccordionContent>
        </AccordionItem>

        <AccordionItem value="packages">
          <AccordionTrigger className="px-2 text-sm">Packages and variables</AccordionTrigger>
          <AccordionContent className="px-2">
            <Items
              items={[
                "Add npm packages to package.json. They install when you Test or Deploy.",
                'Native packages (bcrypt, sharp, …) need "Allow package install scripts" under package.json.',
                "Variables arrive in ctx.env. Secret-backed values are masked in logs.",
                "Use import (ES modules): package.json has \"type\": \"module\".",
              ]}
            />
          </AccordionContent>
        </AccordionItem>

        <AccordionItem value="debugging">
          <AccordionTrigger className="px-2 text-sm">Debugging</AccordionTrigger>
          <AccordionContent className="px-2">
            <Items
              items={[
                "ctx.log.info(message, data) and console.log both land on the run's log.",
                "Run details show Warm (reused) or Cold start, and why a sandbox was replaced after a call (e.g. a timer or request left running).",
                "Use Replay on a run to repeat it with the same input, on the live version.",
              ]}
            />
          </AccordionContent>
        </AccordionItem>
      </Accordion>
    </CardContent>
  </Card>
);
