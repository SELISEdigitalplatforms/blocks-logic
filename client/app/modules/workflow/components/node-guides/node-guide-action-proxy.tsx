import { GuideContent } from "./node-guide-content";

export const NodeGuideActionProxy = () => (
  <GuideContent
    title="Proxy action"
    description="Use this node to call an endpoint through a proxy configured in the Proxy module. The upstream URL, the injected headers and credentials, and any response filtering all stay in the proxy, so the workflow never holds a third-party key."
    steps={[
      "Pick the Proxy. Only enabled proxies are listed.",
      "Pick the Endpoint. The list is the proxy's declared route allowlist, shown as method and path.",
      "Fill in any Path parameters the endpoint declares. Each one fills a single {name} segment and accepts expressions.",
      "Turn on Send Body for POST, PUT or PATCH endpoints and enter a JSON body.",
      "Run the workflow and confirm the response shape before mapping it into later nodes.",
    ]}
    notes={[
      "The node calls the proxy in-process, so no bearer token or tenant key is needed.",
      "Only endpoints on the proxy's allowlist can be called; anything else is refused by the gateway.",
      "Calls are recorded in that proxy's execution log, marked as coming from a workflow and carrying the workflow, run and node.",
      "The node sends one request for each input item.",
      "A JSON array response becomes multiple workflow output items.",
      "If the endpoint filters its response, the node only sees the fields it keeps.",
      "Changing the proxy clears the endpoint, and changing the endpoint clears its path parameters.",
    ]}
  />
);
