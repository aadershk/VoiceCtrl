import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "../design/design.css";
import "./setup.css";
import { createBridge, webViewTransport } from "../bridge/bridge";
import { createMockHost } from "../bridge/mock";
import type { Theme } from "../bridge/contracts.g";
import { markFirstPaintDone } from "../design/motion";
import { installSetupMock, scenarioFromQuery } from "./mockSetup";
import { Setup } from "./Setup";

const root = document.getElementById("root");
if (!root) throw new Error("setup.html has no #root");

// In the app, WebView2's transport; in a plain browser (npm run dev), the mock host, set up from
// the URL (e.g. ?theme=dark&key=didNotWork), so every state can be reached and screenshotted.
function mockTransport() {
  const query = new URLSearchParams(location.search);
  const theme: Theme = query.get("theme") === "dark" ? "dark" : "light";
  const host = createMockHost({ theme, themePreference: theme });
  installSetupMock(host, scenarioFromQuery(location.search));
  return host.transport;
}

const bridge = createBridge(webViewTransport() ?? mockTransport());

// Nothing that depends on the host renders before the snapshot (spec §4).
const app = await bridge.ready();
const setup = await bridge.call("setup.state", {});
document.documentElement.dataset.theme = app.theme;
createRoot(root).render(
  <StrictMode>
    <Setup bridge={bridge} app={app} setup={setup} />
  </StrictMode>,
);
markFirstPaintDone();
