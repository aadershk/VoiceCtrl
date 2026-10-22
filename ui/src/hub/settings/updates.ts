/**
 * The Automatic updates row's state line (DESIGN §12). Times come from the host as Unix
 * milliseconds and read in the user's locale, like the Dictations screen's.
 */
import type { UpdateView } from "../../bridge/contracts.g";
import { clock, dayLabel } from "../dictations/format";

/** "today at 09:41", "yesterday at 09:41", "on Monday at 09:41", "on 14 September at 09:41". */
export function checkedAt(ms: number, now: number, locale?: string): string {
  const day = dayLabel(ms, now, locale);
  const when = day === "Today" || day === "Yesterday" ? day.toLowerCase() : `on ${day}`;
  return `${when} at ${clock(ms, locale)}`;
}

export function updateLine(view: UpdateView, now: number, locale?: string): string {
  const version = view.newVersion ?? "";
  switch (view.state) {
    case "checking":
      return "Checking for a new version.";
    case "downloading":
      return `Downloading ${version} in the background.`;
    case "ready":
      return `${version} is ready. It installs next time VoiceCtrl starts.`;
    case "available":
      return `${version} is out. You're on ${view.currentVersion}.`;
    case "off":
      return `Off. You stay on ${view.currentVersion} until you check.`;
    case "upToDate":
      if (view.checkFailed) {
        return view.lastChecked === null
          ? "Couldn't reach GitHub. VoiceCtrl tries again later."
          : `Couldn't reach GitHub. Last checked ${checkedAt(view.lastChecked, now, locale)}.`;
      }
      return view.lastChecked === null
        ? "New versions download quietly and install the next time VoiceCtrl starts."
        : `Up to date. Checked ${checkedAt(view.lastChecked, now, locale)}.`;
  }
}
