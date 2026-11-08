import { GuideCode, GuideContent, GuideInlineCode as C } from "./node-guide-content";
import { renderRichText } from "@/lib/rich-text";
import { useT } from "@seliseblocks/genesis-os";

const EACH_RESHAPE = `return {
  fullName: $json.firstName + " " + $json.lastName,
  email: $json.email.toLowerCase(),
};`;

const EACH_SPLIT = `// One input item with an "orders" array → one output item per order.
return $json.orders.map(order => ({
  orderId: order.id,
  customer: $json.name,
}));`;

const EACH_EARLIER_NODE = `return {
  ...$json,
  requestedBy: $node["Webhook"].json.email,
};`;

const ALL_MAP = `return $items.map(item => ({
  ...item.json,
  total: item.json.price * item.json.quantity,
}));`;

const ALL_FILTER = `return $items.filter(item => item.json.status === "active");`;

const ALL_AGGREGATE = `const total = $items.reduce((sum, item) => sum + item.json.amount, 0);

return {
  total,
  count: $items.length,
  // Optional: link the result to the first input item so later nodes
  // can still read values from earlier nodes (see "Item links").
  __id: $items[0].json.__id,
};`;

const ALL_EARLIER_NODE = `const settings = $node["Get Settings"].first().json;

return $items.map(item => ({
  ...item.json,
  currency: settings.currency,
}));`;

const ALL_LOOKUP = `const usersById = {};
for (const user of $node["Get Users"].all()) {
  usersById[user.json.id] = user.json;
}

return $items.map(item => ({
  ...item.json,
  userName: usersById[item.json.userId]?.name ?? null,
}));`;

const ALL_SHAPE = `// $items and $node["Name"].all() return a list like this:
[
  { json: { name: "Alice", age: 30, __id: "..." } },
  { json: { name: "Bob",   age: 25, __id: "..." } },
]`;

const code = (text: string) => <C>{text}</C>;
const strong = (text: string) => <strong>{text}</strong>;
const em = (text: string) => <em>{text}</em>;

export const NodeGuideTransformCodeV1 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_TRANSFORM_CODE_V1.CODE_TRANSFORM")}
      description={t("NODE_GUIDE_TRANSFORM_CODE_V1.USE_THIS_NODE_TO")}
      steps={[
        <>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.STEP_MODE"), [strong, strong, strong])}</>,
        t("NODE_GUIDE_TRANSFORM_CODE_V1.LEAVE_LANGUAGE_SET_TO"),
        <>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.STEP_READ"), [strong, code])}</>,
        <>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.STEP_RETURN"), [code, code])}</>,
        t("NODE_GUIDE_TRANSFORM_CODE_V1.RUN_THE_NODE_WITH"),
      ]}
      sections={[
        {
          title: t("NODE_GUIDE_TRANSFORM_CODE_V1.VARIABLES_BY_MODE"),
          content: (
            <div className="space-y-3">
              <div className="space-y-1">
                <p className="font-medium text-foreground">{t("NODE_GUIDE_TRANSFORM_CODE_V1.RUN_ONCE_FOR_EACH")}</p>
                <ul className="list-disc space-y-1 pl-5">
                  <li>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.EACH_JSON"), [code, code, code])}</li>
                  <li>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.EACH_NODE"), [code, em, code])}</li>
                </ul>
              </div>
              <div className="space-y-1">
                <p className="font-medium text-foreground">{t("NODE_GUIDE_TRANSFORM_CODE_V1.RUN_ONCE_FOR_ALL")}</p>
                <ul className="list-disc space-y-1 pl-5">
                  <li>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.ALL_ITEMS"), [code, code, code, code, code, code])}</li>
                  <li>
                    {renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.ALL_NODE"), [code])}
                    <ul className="mt-1 list-[circle] space-y-1 pl-5">
                      <li>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.ALL_FN_ALL"), [code])}</li>
                      <li>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.ALL_FN_FIRST_LAST"), [code, code])}</li>
                      <li>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.ALL_FN_ITEM"), [code, code])}</li>
                    </ul>
                  </li>
                  <li>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.ALL_WRAPPED"), [code, code])}</li>
                </ul>
                <GuideCode>{ALL_SHAPE}</GuideCode>
              </div>
              <p>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.EXISTS_ONLY"), [code, code, code])}</p>
            </div>
          ),
        },
        {
          title: t("NODE_GUIDE_TRANSFORM_CODE_V1.READING_EARLIER_NODES"),
          content: (
            <div className="space-y-2">
              <p>{t("NODE_GUIDE_TRANSFORM_CODE_V1.YOU_CAN_READ_ANY")}</p>
              <p>{t("NODE_GUIDE_TRANSFORM_CODE_V1.THE_SAME_FIELD_IS")}</p>
              <ul className="list-disc space-y-1 pl-5">
                <li>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.EXPRESSION_FIELD_IN_OTHER"), [code])}</li>
                <li>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.CODE_EACH_ITEM_MODE"), [code])}</li>
                <li>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.CODE_ALL_ITEMS_MODE"), [code])}</li>
              </ul>
              <p>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.MISSING_NODE"), [code, code, code])}</p>
            </div>
          ),
        },
        {
          title: t("NODE_GUIDE_TRANSFORM_CODE_V1.ITEM_LINKS_ID"),
          content: (
            <div className="space-y-2">
              <p>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.LINKS_INTRO"), [code])}</p>
              <ul className="list-disc space-y-1 pl-5">
                <li>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.N_0_EACH_ITEM_MODE"), [strong])}</li>
                <li>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.LINKS_ALL_ITEMS"), [strong, code, code, code, code, code])}</li>
                <li>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.A_NEW_OBJECT_WITHOUT"), [code, em, code])}</li>
              </ul>
            </div>
          ),
        },
        {
          title: t("NODE_GUIDE_TRANSFORM_CODE_V1.EXAMPLES_EACH_ITEM_MODE"),
          content: (
            <div className="space-y-2">
              <p>{t("NODE_GUIDE_TRANSFORM_CODE_V1.BUILD_A_NEW_OBJECT")}</p>
              <GuideCode>{EACH_RESHAPE}</GuideCode>
              <p>{t("NODE_GUIDE_TRANSFORM_CODE_V1.SPLIT_ONE_ITEM_INTO")}</p>
              <GuideCode>{EACH_SPLIT}</GuideCode>
              <p>{t("NODE_GUIDE_TRANSFORM_CODE_V1.ADD_A_VALUE_FROM")}</p>
              <GuideCode>{EACH_EARLIER_NODE}</GuideCode>
            </div>
          ),
        },
        {
          title: t("NODE_GUIDE_TRANSFORM_CODE_V1.EXAMPLES_ALL_ITEMS_MODE"),
          content: (
            <div className="space-y-2">
              <p>{t("NODE_GUIDE_TRANSFORM_CODE_V1.ADD_A_FIELD_TO")}</p>
              <GuideCode>{ALL_MAP}</GuideCode>
              <p>{t("NODE_GUIDE_TRANSFORM_CODE_V1.KEEP_ONLY_MATCHING_ITEMS")}</p>
              <GuideCode>{ALL_FILTER}</GuideCode>
              <p>{t("NODE_GUIDE_TRANSFORM_CODE_V1.COMBINE_ALL_ITEMS_INTO")}</p>
              <GuideCode>{ALL_AGGREGATE}</GuideCode>
              <p>{t("NODE_GUIDE_TRANSFORM_CODE_V1.USE_A_VALUE_FROM")}</p>
              <GuideCode>{ALL_EARLIER_NODE}</GuideCode>
              <p>{t("NODE_GUIDE_TRANSFORM_CODE_V1.MATCH_ITEMS_AGAINST_ANOTHER")}</p>
              <GuideCode>{ALL_LOOKUP}</GuideCode>
            </div>
          ),
        },
      ]}
      notes={[
        t("NODE_GUIDE_TRANSFORM_CODE_V1.ONLY_JAVASCRIPT_IS_SUPPORTED"),
        t("NODE_GUIDE_TRANSFORM_CODE_V1.CODE_RUNS_IN_A"),
        t("NODE_GUIDE_TRANSFORM_CODE_V1.IF_THE_SCRIPT_RETURNS"),
        <>{renderRichText(t("NODE_GUIDE_TRANSFORM_CODE_V1.NOTE_EACH_ITEM_ERROR"), [code])}</>,
      ]}
    />
  );
};
