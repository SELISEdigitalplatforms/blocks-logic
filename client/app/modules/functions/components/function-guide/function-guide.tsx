import { ReactNode } from "react";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui-kits/accordion/accordion";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { SANDBOX_CODE_RULES } from "../../constants/limits.constant";
import { renderRichText } from "@/lib/rich-text";
import { useT } from "@seliseblocks/genesis-os";

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

export const FunctionGuide = () => {
  const { t } = useT("logic.functions.components");
  return (
    <Card>
      <CardContent className="p-2">
        <Accordion type="multiple" defaultValue={["handler", "speed"]} data-testid="function-guide">
          <AccordionItem value="handler">
            <AccordionTrigger className="px-2 text-sm">{t("FUNCTION_GUIDE.THE_HANDLER")}</AccordionTrigger>
            <AccordionContent className="flex flex-col gap-2 px-2">
              <Block>{`export default async function handler(input, ctx) {
    return { ok: true }; // becomes the run's result
  }`}</Block>
              <Items
                items={[
                  <>
                    {renderRichText(t("FUNCTION_GUIDE.N_0_INPUT_0_1"), [(children) => <Code>{children}</Code>, (children) => <Code>{children}</Code>, (children) => <Code>{children}</Code>, (children) => <Code>{children}</Code>, (children) => <Code>{children}</Code>, (children) => <Code>{children}</Code>, (children) => <Code>{children}</Code>])}
                  </>,
                  <>
                    {renderRichText(t("FUNCTION_GUIDE.N_0_CTX_0_1"), [(children) => <Code>{children}</Code>, (children) => <Code>{children}</Code>, (children) => <Code>{children}</Code>, (children) => <Code>{children}</Code>, (children) => <Code>{children}</Code>, (children) => <Code>{children}</Code>, (children) => <Code>{children}</Code>])}
                  </>,
                  <>
                    {renderRichText(t("FUNCTION_GUIDE.N_0_INPUT_0_AND"), [(children) => <Code>{children}</Code>, (children) => <Code>{children}</Code>])}
                  </>,
                  <>
                    {renderRichText(t("FUNCTION_GUIDE.GET_THE_CALLER_S"), [(children) => <Code>{children}</Code>, (children) => <Code>{children}</Code>])}
                  </>,
                ]}
              />
            </AccordionContent>
          </AccordionItem>

          <AccordionItem value="speed">
            <AccordionTrigger className="px-2 text-sm">{t("FUNCTION_GUIDE.SPEED_THE_SANDBOX_IS")}</AccordionTrigger>
            <AccordionContent className="flex flex-col gap-2 px-2">
              <Items
                items={[
                  t("FUNCTION_GUIDE.CALLS_OF_A_DEPLOYED"),
                  t("FUNCTION_GUIDE.CREATE_DATABASE_AND_HTTP"),
                  t("FUNCTION_GUIDE.WITH_REUSE_ON_EVERY"),
                  t("FUNCTION_GUIDE.TEST_RUNS_ALWAYS_START"),
                  t("FUNCTION_GUIDE.A_SANDBOX_THAT_SITS"),
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
            <AccordionTrigger className="px-2 text-sm">{t("FUNCTION_GUIDE.RULES_THAT_KEEP_IT")}</AccordionTrigger>
            <AccordionContent className="flex flex-col gap-2 px-2">
              <P>
                {t("FUNCTION_GUIDE.THE_NEXT_CALL_IN")}
              </P>
              <Items items={SANDBOX_CODE_RULES.map((rule) => `Don't ${rule.dont.charAt(0).toLowerCase()}${rule.dont.slice(1)} ${rule.fix}`)} />
            </AccordionContent>
          </AccordionItem>

          <AccordionItem value="api">
            <AccordionTrigger className="px-2 text-sm">{t("FUNCTION_GUIDE.ANSWERING_AS_AN_API")}</AccordionTrigger>
            <AccordionContent className="flex flex-col gap-2 px-2">
              <P>
                {renderRichText(t("FUNCTION_GUIDE.WITH_RESPONSE_0_WAIT"), [(children) => <strong>{children}</strong>])}
              </P>
              <Block>{`return {
    statusCode: 201,
    headers: { "content-type": "application/json" },
    body: { id: order.id },
  };`}</Block>
              <Items
                items={[
                  t("FUNCTION_GUIDE.ANYTHING_ELSE_YOU_RETURN"),
                  t("FUNCTION_GUIDE.HEADERS_THAT_PASS_CONTENT"),
                  t("FUNCTION_GUIDE.A_FAILED_RUN_ANSWERS"),
                  t("FUNCTION_GUIDE.ACCEPTED_METHODS_GET_POST"),
                ]}
              />
            </AccordionContent>
          </AccordionItem>

          <AccordionItem value="webhooks">
            <AccordionTrigger className="px-2 text-sm">{t("FUNCTION_GUIDE.RECEIVING_WEBHOOKS")}</AccordionTrigger>
            <AccordionContent className="flex flex-col gap-2 px-2">
              <P>
                {renderRichText(t("FUNCTION_GUIDE.VERIFY_A_WEBHOOK_WITH"), [(children) => <Code>{children}</Code>, (children) => <Code>{children}</Code>])}
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
                  t("FUNCTION_GUIDE.SIGNATURE_HEADERS_THAT_REACH"),
                  "rawBody is null when there is no body, or when the body is too large to carry twice.",
                  t("FUNCTION_GUIDE.SET_THE_TRIGGER_TO"),
                ]}
              />
            </AccordionContent>
          </AccordionItem>

          <AccordionItem value="streaming">
            <AccordionTrigger className="px-2 text-sm">{t("FUNCTION_GUIDE.STREAMING_ANSWERS_AI")}</AccordionTrigger>
            <AccordionContent className="flex flex-col gap-2 px-2">
              <P>
                {renderRichText(t("FUNCTION_GUIDE.WITH_RESPONSE_0_WAIT_2"), [(children) => <strong>{children}</strong>, (children) => <strong>{children}</strong>])}
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
                  t("FUNCTION_GUIDE.YIELD_STRINGS_SENT_AS"),
                  t("FUNCTION_GUIDE.WITHOUT_ACCEPT_TEXT_EVENT"),
                  t("FUNCTION_GUIDE.WITH_RESPONSE_STREAM_EVENTS"),
                  t("FUNCTION_GUIDE.A_STREAMED_ANSWER_HAS"),
                  t("FUNCTION_GUIDE.IN_THE_EDITOR_A"),
                ]}
              />
            </AccordionContent>
          </AccordionItem>

          <AccordionItem value="limits">
            <AccordionTrigger className="px-2 text-sm">{t("FUNCTION_GUIDE.LIMITS")}</AccordionTrigger>
            <AccordionContent className="px-2">
              <Items
                items={[
                  t("FUNCTION_GUIDE.EACH_CALL_HAS_A"),
                  t("FUNCTION_GUIDE.FIXED_MEMORY_PER_SANDBOX"),
                  t("FUNCTION_GUIDE.THE_REQUEST_BODY_THE"),
                  t("FUNCTION_GUIDE.READ_ONLY_FILESYSTEM_EXCEPT"),
                  t("FUNCTION_GUIDE.PROCESSES_PER_SANDBOX_AND"),
                  t("FUNCTION_GUIDE.API_MODE_WAITS_A"),
                  t("FUNCTION_GUIDE.HTTP_CALLS_PER_MINUTE"),
                ]}
              />
            </AccordionContent>
          </AccordionItem>

          <AccordionItem value="network">
            <AccordionTrigger className="px-2 text-sm">{t("FUNCTION_GUIDE.NETWORK")}</AccordionTrigger>
            <AccordionContent className="px-2">
              <Items
                items={[
                  t("FUNCTION_GUIDE.OUTBOUND_CALLS_GO_TO"),
                  t("FUNCTION_GUIDE.PRIVATE_AND_INTERNAL_ADDRESSES"),
                  t("FUNCTION_GUIDE.USE_A_PUBLIC_ENDPOINT"),
                ]}
              />
            </AccordionContent>
          </AccordionItem>

          <AccordionItem value="packages">
            <AccordionTrigger className="px-2 text-sm">{t("FUNCTION_GUIDE.PACKAGES_AND_VARIABLES")}</AccordionTrigger>
            <AccordionContent className="px-2">
              <Items
                items={[
                  t("FUNCTION_GUIDE.ADD_NPM_PACKAGES_TO"),
                  t("FUNCTION_GUIDE.NATIVE_PACKAGES_BCRYPT_SHARP"),
                  t("FUNCTION_GUIDE.VARIABLES_ARRIVE_IN_CTX"),
                  t("FUNCTION_GUIDE.USE_IMPORT_ES_MODULES"),
                ]}
              />
            </AccordionContent>
          </AccordionItem>

          <AccordionItem value="debugging">
            <AccordionTrigger className="px-2 text-sm">{t("FUNCTION_GUIDE.DEBUGGING")}</AccordionTrigger>
            <AccordionContent className="px-2">
              <Items
                items={[
                  "ctx.log.info(message, data) and console.log both land on the run's log.",
                  t("FUNCTION_GUIDE.RUN_DETAILS_SHOW_WARM"),
                  t("FUNCTION_GUIDE.USE_REPLAY_ON_A"),
                ]}
              />
            </AccordionContent>
          </AccordionItem>
        </Accordion>
      </CardContent>
    </Card>
  );
};
