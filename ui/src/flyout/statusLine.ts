import type { AppStatus } from "../bridge/contracts.g";
import { readyHint } from "../design/hotkeyCopy";
import { statusCardState } from "../hub/statusCard";

const noActions = { openSettingsRow: () => undefined, resume: () => undefined };

/**
 * The flyout header's status line (DESIGN §12). Ready and Paused have their own words; a warning
 * reads as the status card's title and fix, taken from the card itself so the two never disagree.
 */
export function flyoutStatusLine(status: AppStatus): string {
  switch (status.kind) {
    case "ready":
      return `Ready · ${readyHint(status.hotkey, status.talkKeys)}`;
    case "paused":
      return status.talkKeys ? "Paused · Your hotkeys do nothing" : `Paused · The ${status.hotkey} double-tap does nothing`;
    default: {
      const card = statusCardState(status, noActions);
      return card.kind === "warning" ? `${card.title} · ${card.fix}` : "";
    }
  }
}
