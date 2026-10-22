import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "../design/design.css";
import "./hub.css";
import { createBridge, webViewTransport } from "../bridge/bridge";
import { createMockHost } from "../bridge/mock";
import type { AppSnapshot, AppStatusKind, Theme } from "../bridge/contracts.g";
import { markFirstPaintDone } from "../design/motion";
import { Hub } from "./Hub";
import { dictationsScenarioFromQuery, installDictationsMock } from "./mockDictations";
import { generalScenarioFromQuery, installGeneralMock } from "./mockGeneral";
import { installPersonalizationMock, personalizationFromQuery } from "./mockPersonalization";
import { installProfilesMock, profilesFromQuery } from "./mockProfiles";
import { installSettingsMock, settingsScenarioFromQuery } from "./mockSettings";
import { installStyleMock, styleFromQuery } from "./mockStyle";
import { installTranscriptionMock, transcriptionScenarioFromQuery } from "./mockTranscription";
import { startTargetFromQuery } from "./screens";

const root = document.getElementById("root");
if (!root) throw new Error("hub.html has no #root");

/**
 * Mock-host fixtures from the address, for `npm run dev` and the visual tests only:
 * `hub.html?theme=dark&status=noMicrophone&offline=1&updated=4.1.0`. The app's own queries are
 * only `screen` and `row`.
 */
function mockSnapshot(): Partial<AppSnapshot> {
  const query = new URLSearchParams(location.search);
  const theme: Theme = query.get("theme") === "dark" ? "dark" : "light";
  const kind = (query.get("status") ?? "ready") as AppStatusKind;
  document.documentElement.dataset.theme = theme;
  return {
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
    updatedTo: query.get("updated"),
  };
}

function mockHost() {
  const host = createMockHost(mockSnapshot());
  installSettingsMock(host, settingsScenarioFromQuery(location.search));
  installTranscriptionMock(host, transcriptionScenarioFromQuery(location.search));
  installDictationsMock(host, dictationsScenarioFromQuery(location.search));
  installGeneralMock(host, generalScenarioFromQuery(location.search));
  installPersonalizationMock(host, personalizationFromQuery(location.search));
  installProfilesMock(host, profilesFromQuery(location.search));
  installStyleMock(host, styleFromQuery(location.search));
  return host;
}

// In the app, WebView2's transport; in a plain browser (npm run dev), the mock host.
const bridge = createBridge(webViewTransport() ?? mockHost().transport);

// Nothing that depends on the host renders before the snapshot (spec §4).
const snapshot = await bridge.ready();
createRoot(root).render(
  <StrictMode>
    <Hub bridge={bridge} snapshot={snapshot} start={startTargetFromQuery(location.search)} />
  </StrictMode>,
);
markFirstPaintDone();
