import type { AppStatus } from "../bridge/contracts.g";
import type { StatusCardState } from "../design/components";

export interface StatusActions {
  /** Opens Settings on a row (the fix for a warning). */
  openSettingsRow(row: string): void;
  resume(): void;
}

/**
 * The status card's words for each state Core reports (research 10 "Status card", DESIGN §8).
 * Every warning links to its fix; Paused is the user's own choice, so it reads neutral.
 */
export function statusCardState(status: AppStatus, actions: StatusActions): StatusCardState {
  switch (status.kind) {
    case "ready":
      return { kind: "ready", hotkey: status.hotkey, talkKeys: status.talkKeys };
    case "paused":
      return { kind: "warning", tone: "neutral", icon: "pause", title: "Paused", fix: "Resume", onFix: actions.resume };
    case "noMicrophone":
      return {
        kind: "warning",
        icon: "micOff",
        title: "No microphone",
        fix: "Choose one in Settings",
        onFix: () => actions.openSettingsRow("microphone"),
      };
    case "apiKeyNotWorking":
      return {
        kind: "warning",
        icon: "key",
        title: `${status.provider} key not working`,
        // Auto falls back to Offline meanwhile; Online can't, so it just points at the fix.
        fix: status.offlineFallback ? "Using Offline until it's fixed" : "Fix it in Settings",
        onFix: () => actions.openSettingsRow("api-key"),
      };
  }
}
