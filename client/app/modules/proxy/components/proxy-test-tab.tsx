import { useState } from "react";
import { useSendProxyTestRequest } from "../hooks";
import { getProxyGatewayPath } from "../constants";
import { Proxy, ProxyMethod, ProxyTestResponse } from "../types";
import { ProxyTestPanel } from "./proxy-test-panel";

const methodsWithBody: ProxyMethod[] = ["POST", "PUT", "PATCH"];

/**
 * Details-page Test tab. Unlike the form's panel this always tests the *saved* proxy (`proxyId`),
 * so the method picker is limited to the methods the proxy has enabled — the server rejects any
 * other method (ProxyTestService). Test runs never write a Request logs row.
 */
export const ProxyTestTab = ({ proxy }: { proxy: Proxy }) => {
  const [method, setMethod] = useState<ProxyMethod>(proxy.methods[0] ?? "GET");
  const [pathSuffix, setPathSuffix] = useState("/");
  const [body, setBody] = useState("");
  const [response, setResponse] = useState<ProxyTestResponse | null>(null);
  const sendTest = useSendProxyTestRequest();

  const runTest = async () => {
    const res = await sendTest.mutateAsync({
      proxyId: proxy.id,
      method,
      pathSuffix,
      body: methodsWithBody.includes(method) ? body : undefined,
      contentType: "application/json",
    });
    setResponse(res);
  };

  return (
    <ProxyTestPanel
      title="Test this proxy"
      description={
        proxy.enabled
          ? "Sends a real request through this proxy with its saved headers, query parameters and body fields. Test runs are not written to Request logs."
          : "This proxy is paused, so the gateway answers 404 until it is resumed. Test runs are not written to Request logs."
      }
      methods={proxy.methods}
      method={method}
      onMethodChange={(next) => setMethod(next)}
      pathPrefix={getProxyGatewayPath(proxy.slug)}
      pathSuffix={pathSuffix}
      onPathSuffixChange={setPathSuffix}
      showBody={methodsWithBody.includes(method)}
      body={body}
      onBodyChange={setBody}
      onSend={runTest}
      sending={sendTest.isPending}
      response={response}
    />
  );
};
