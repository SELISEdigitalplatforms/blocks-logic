import { GuideCode, GuideContent, GuideInlineCode as C } from "./node-guide-content";

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

export const NodeGuideTransformCodeV1 = () => (
  <GuideContent
    title="Code transform"
    description="Use this node to reshape, filter, combine, split, or calculate workflow data with JavaScript. Whatever the script returns becomes this node's output items."
    steps={[
      <>
        Choose a <strong>Mode</strong>. <strong>Run Once for Each Item</strong> runs the script
        separately for every input item and is the simplest choice for per-item changes.{" "}
        <strong>Run Once for All Items</strong> runs the script a single time with the whole input
        list, for filtering, totals, grouping, or lookups across items.
      </>,
      "Leave Language set to JavaScript.",
      <>
        Read input data with the variables for your mode (see <strong>Variables by mode</strong>{" "}
        below). Write JavaScript directly. Expression syntax such as{" "}
        <C>{"{{$json.output.name}}"}</C> does not work inside the script.
      </>,
      <>
        End the script with <C>return</C>. Return an object to produce one item or an array of
        objects to produce several items. Return <C>[]</C> to produce no items.
      </>,
      "Run the node with real upstream data and check the output before connecting later nodes.",
    ]}
    sections={[
      {
        title: "Variables by mode",
        content: (
          <div className="space-y-3">
            <div className="space-y-1">
              <p className="font-medium text-foreground">Run Once for Each Item</p>
              <ul className="list-disc space-y-1 pl-5">
                <li>
                  <C>$json</C> is the current item&apos;s data. Read fields directly:{" "}
                  <C>$json.name</C>. <C>$item</C> is the same value.
                </li>
                <li>
                  <C>{'$node["Node name"].json'}</C> is the item from an earlier node that{" "}
                  <em>this</em> item came from, for example <C>{'$node["Webhook"].json.email'}</C>.
                </li>
              </ul>
            </div>
            <div className="space-y-1">
              <p className="font-medium text-foreground">Run Once for All Items</p>
              <ul className="list-disc space-y-1 pl-5">
                <li>
                  <C>$items</C> is the list of input items. Each entry wraps its data in <C>json</C>
                  , so read fields as <C>$items[0].json.name</C> or <C>item.json.name</C> inside{" "}
                  <C>map</C>/<C>filter</C>.
                </li>
                <li>
                  <C>{'$node["Node name"]'}</C> gives access to all items of any earlier node:
                  <ul className="mt-1 list-[circle] space-y-1 pl-5">
                    <li>
                      <C>.all()</C>: every item, as a list
                    </li>
                    <li>
                      <C>.first()</C> / <C>.last()</C>: the first or last item
                    </li>
                    <li>
                      <C>.item(i)</C>: the item at position <C>i</C> (starting at 0)
                    </li>
                  </ul>
                </li>
                <li>
                  Items returned by these functions are wrapped in <C>json</C> too:{" "}
                  <C>{'$node["Webhook"].first().json.email'}</C>.
                </li>
              </ul>
              <GuideCode>{ALL_SHAPE}</GuideCode>
            </div>
            <p>
              <C>$json</C> exists only in each-item mode. <C>$items</C> and the <C>.all()</C>{" "}
              functions exist only in all-items mode.
            </p>
          </div>
        ),
      },
      {
        title: "Reading earlier nodes",
        content: (
          <div className="space-y-2">
            <p>
              You can read any node that runs before this one, not just the node directly connected
              to it. Use the node&apos;s name exactly as it appears on the canvas (names are
              case-sensitive).
            </p>
            <p>The same field is written differently in expression fields and in code:</p>
            <ul className="list-disc space-y-1 pl-5">
              <li>
                Expression field in other nodes: <C>{'{{$node["Webhook"].json.output.email}}'}</C>
              </li>
              <li>
                Code, each-item mode: <C>{'$node["Webhook"].json.email'}</C>
              </li>
              <li>
                Code, all-items mode: <C>{'$node["Webhook"].first().json.email'}</C>
              </li>
            </ul>
            <p>
              A node that produced no items, or that is not part of the current item&apos;s path, is
              missing from <C>$node</C>. Use <C>?.</C> when unsure:{" "}
              <C>{'$node["Webhook"]?.json?.email'}</C>.
            </p>
          </div>
        ),
      },
      {
        title: "Item links (__id)",
        content: (
          <div className="space-y-2">
            <p>
              Every output item remembers which input item it came from. Later nodes use that link
              to resolve expressions such as <C>{'{{$node["Webhook"].json.output.email}}'}</C> for
              the right item.
            </p>
            <ul className="list-disc space-y-1 pl-5">
              <li>
                <strong>Each-item mode</strong> links every output to its input automatically.
              </li>
              <li>
                <strong>All-items mode</strong> links by the hidden <C>__id</C> field that every
                input item carries in <C>item.json.__id</C>. Returning <C>item</C>, <C>item.json</C>
                , or <C>{"{ ...item.json, ... }"}</C> keeps it. The field is removed from the saved
                output.
              </li>
              <li>
                A new object without <C>__id</C> (a total, for example) is linked to <em>all</em>{" "}
                input items. It still works, but when those inputs came from different earlier
                items, later nodes read values from those earlier nodes as empty. Add{" "}
                <C>__id: $items[0].json.__id</C> (or the id of the item it belongs to) to pick one.
              </li>
            </ul>
          </div>
        ),
      },
      {
        title: "Examples: each-item mode",
        content: (
          <div className="space-y-2">
            <p>Build a new object from the current item:</p>
            <GuideCode>{EACH_RESHAPE}</GuideCode>
            <p>Split one item into several:</p>
            <GuideCode>{EACH_SPLIT}</GuideCode>
            <p>Add a value from an earlier node:</p>
            <GuideCode>{EACH_EARLIER_NODE}</GuideCode>
          </div>
        ),
      },
      {
        title: "Examples: all-items mode",
        content: (
          <div className="space-y-2">
            <p>Add a field to every item (keeps item links):</p>
            <GuideCode>{ALL_MAP}</GuideCode>
            <p>Keep only matching items:</p>
            <GuideCode>{ALL_FILTER}</GuideCode>
            <p>Combine all items into one:</p>
            <GuideCode>{ALL_AGGREGATE}</GuideCode>
            <p>Use a value from an earlier node on every item:</p>
            <GuideCode>{ALL_EARLIER_NODE}</GuideCode>
            <p>Match items against another node&apos;s list:</p>
            <GuideCode>{ALL_LOOKUP}</GuideCode>
          </div>
        ),
      },
    ]}
    notes={[
      "Only JavaScript is supported.",
      "Code runs in a sandbox for data transformation, so external API calls, file access, package imports, and system commands are not available.",
      "If the script returns nothing or an empty array, the node produces no output items.",
      <>
        In each-item mode, an error in one item does not stop the node. That item&apos;s output
        becomes <C>{"{ error: true, message }"}</C> and the remaining items still run. In all-items
        mode, an error fails the node.
      </>,
    ]}
  />
);
