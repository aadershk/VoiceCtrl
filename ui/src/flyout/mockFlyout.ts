import type { MockHost } from "../bridge/mock";
import type { TranscriptionModePreference, TrayCorner, TrayOpening } from "../bridge/contracts.g";

/** Mock fixtures from the address, for `npm run dev` and the visual tests only:
 * `flyout.html?theme=dark&status=paused&mode=offline&latest=0&corner=topLeft`. */
export function flyoutScenarioFromQuery(search: string): TrayOpening {
  const query = new URLSearchParams(search);
  return {
    corner: (query.get("corner") ?? "bottomRight") as TrayCorner,
    mode: (query.get("mode") ?? "auto") as TranscriptionModePreference,
    hasLatest: query.get("latest") !== "0",
  };
}

/** The tray.* commands, answered as the host would. */
export function installFlyoutMock(host: MockHost): void {
  for (const name of ["tray.place", "tray.close", "tray.openHub", "tray.openSettings", "tray.copyLatest", "tray.quit"] as const) {
    host.handle(name, () => null);
  }
  host.handle("settings.setMode", () => null);
}
