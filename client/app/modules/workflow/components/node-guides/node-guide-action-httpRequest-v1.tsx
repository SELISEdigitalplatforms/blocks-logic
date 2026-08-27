import { GuideContent } from "./node-guide-content";

export const NodeGuideActionHttpRequestV1 = () => (
  <GuideContent
    title="HTTP Request action"
    description="Use this node to call an external HTTP endpoint from the workflow. It can send query parameters, headers, and a JSON body when those sections are enabled."
    steps={[
      "Choose the request Method and enter the target URL. Expressions in the URL are resolved for each input item.",
      "Turn on Send Query Parameters if the endpoint expects URL query values, then add the key-value pairs. Keys and values can use expressions.",
      "Turn on Send Headers for authentication, content negotiation, or other request headers. Header names and values can use expressions.",
      "Turn on Send Body for methods that need a JSON payload, choose JSON, and enter a valid body.",
      "Test the request and confirm the response shape before mapping it into later nodes.",
    ]}
    notes={[
      "The node sends one request for each input item.",
      "The response body must be valid JSON. A JSON array response becomes multiple workflow output items.",
      "Body editing is only shown after Send Body is enabled and the body content type is JSON.",
      "Headers and query parameters are omitted unless their switches are enabled.",
    ]}
  />
);
