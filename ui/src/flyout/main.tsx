import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "../design/design.css";
import "./flyout.css";
import { createBridge, webViewTransport } from "../bridge/bridge";
import { createMockHost } from "../bridge/mock";
import type { MockHost } from "../bridge/mock";
import type { AppStatusKind, Theme } from "../bridge/contracts.g";
import { Flyout } from "./Flyout";
import { flyoutScenarioFromQuery, installFlyoutMock } from "./mockFlyout";

const root = document.getElementById("root");
if (!root) throw new Error("flyout.html has no #root");

/** In a plain browser (npm run dev, the visual tests), the mock host, set up from the address. */
function mockHost() {
  const query = new URLSearchParams(location.search);
  const theme: Theme = query.get("theme") === "dark" ? "dark" : "light";
  const kind = (query.get("status") ?? "ready") as AppStatusKind;
  document.documentElement.dataset.theme = theme;
  const host = createMockHost({
    theme,
    themePreference: theme,
    status: {
      kind,
      hotkey: "Ctrl",
      paused: kind === "paused",
      offlineFallback: query.get("offline") === "1",
      // ?talk=1: the shipped default, Trigger Key on. Without it, the Trigger Key is removed.
      talkKeys: query.get("talk") === "1" ? ["Left Ctrl", "Left Win"] : null,
      provider: query.get("provider") ?? "Gemini",
    },
  });
  installFlyoutMock(host);
  return host;
}

// In the app, WebView2's transport; in a plain browser, the mock host.
const webView = webViewTransport();
const host = webView ? null : mockHost();
const bridge = createBridge(webView ?? (host as MockHost).transport);

// Nothing that depends on the host renders before the snapshot (spec §4). The page then waits,
// hidden, for the host's first tray.open.
const snapshot = await bridge.ready();
createRoot(root).render(
  <StrictMode>
    <Flyout bridge={bridge} snapshot={snapshot} />
  </StrictMode>,
);

// The mock opens the flyout as a right-click on the icon would, repeating it until the page is
// listening and answers, as the host does while the page loads.
if (host) {
  const opening = flyoutScenarioFromQuery(location.search);
  const repeat = setInterval(() => host.emit("tray.open", opening), 50);
  host.handle("tray.place", () => {
    clearInterval(repeat);
    return null;
  });
}
