import { GuideContent } from "./node-guide-content";

export const NodeGuideActionProxy = () => (
  <GuideContent
    title="Proxy action"
    description="Use this node to call a proxy configured in the Proxy module. The upstream URL, the injected headers and query parameters, and any response field filtering all come from the proxy itself, so the workflow never holds the upstream credential."
    steps={[
      "Pick the Proxy. Only enabled proxies are listed.",
      "Choose the Method. The list is narrowed to the methods that proxy allows.",
      "Optionally set a Path, appended after the proxy upstream, for example charges or charges/ch_123. Expressions are resolved for each input item.",
      "Turn on Send Body for POST, PUT or PATCH, choose JSON, and enter a valid body.",
      "Run the workflow and confirm the response shape before mapping it into later nodes.",
    ]}
    notes={[
      "The node calls the proxy in-process, so no bearer token or X-Blocks-Key header is needed.",
      "Every call is recorded in that proxy's execution log, exactly like a call through the public gateway.",
      "The node sends one request for each input item.",
      "A JSON array response becomes multiple workflow output items.",
      "If the proxy sets Response Mode to Select, the node only sees the whitelisted fields.",
      "Changing the selected proxy clears the chosen Method, because the allowed methods differ per proxy.",
    ]}
  />
);
