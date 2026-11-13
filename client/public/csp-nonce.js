// CSP style nonce shim, loaded by the SPA shell before the app. The server writes the response's
// style nonce into meta[name=csp-nonce] (SpaShell) and allows only style elements that carry it
// (style-src 'nonce-...'). genesis-os, Radix, xyflow and the Monaco editor create their style
// elements at runtime through document.createElement, so each one gets the nonce here.
(function () {
  const meta = document.querySelector('meta[name="csp-nonce"]');
  const nonce = meta ? meta.nonce || meta.getAttribute("nonce") : "";
  if (!nonce || nonce.startsWith("__")) return;

  globalThis.__webpack_nonce__ = nonce;

  const createElement = Document.prototype.createElement;
  Document.prototype.createElement = function (tagName, options) {
    const element = createElement.call(this, tagName, options);
    if (typeof tagName === "string" && tagName.toLowerCase() === "style") {
      element.setAttribute("nonce", nonce);
    }
    return element;
  };
})();
