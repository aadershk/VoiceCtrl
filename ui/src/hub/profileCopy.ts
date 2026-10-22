/**
 * The Profiles screen's words (research 10 "Profiles"), kept apart from the screen so the tests can
 * pin them. The stored names are Core's: formatting structured, prose, paragraphs or none; cleanup
 * default, verbatim, light, standard or aggressive.
 */
import type { AppKind, ProfileEditError, ProfileEntry } from "../bridge/contracts.g";
import type { Option } from "../design/components";
import { KIND_LABELS, STYLE_OPTIONS, styleLabel } from "./styleCopy";

export const FORMATTING_OPTIONS: readonly Option<string>[] = [
  { value: "structured", label: "Structured" },
  { value: "prose", label: "Prose" },
  { value: "paragraphs", label: "Paragraphs" },
  { value: "none", label: "None" },
];

export const CLEANUP_OPTIONS: readonly Option<string>[] = [
  { value: "default", label: "Default" },
  { value: "verbatim", label: "Word for word" },
  { value: "light", label: "Light" },
  { value: "standard", label: "Standard" },
  { value: "aggressive", label: "Thorough" },
];

const FORMATTING_SUMMARY: Record<string, string> = { structured: "Structured", prose: "Prose", paragraphs: "Paragraphs", none: "No formatting" };

/** "Standard" for a stored level, as Settings labels it. */
export function cleanupLabel(level: string): string {
  return CLEANUP_OPTIONS.find((o) => o.value === level)?.label ?? "Standard";
}

/** The Style choice (ticket 56): "Same as Work chat" (stored empty), then the four Styles. */
export function styleOptions(kind: AppKind): Option<string>[] {
  return [{ value: "", label: `Same as ${KIND_LABELS[kind]}` }, ...STYLE_OPTIONS];
}

/** Under the Style choice: what "Same as" means for this app. */
export function styleNote(kind: AppKind): string {
  return kind === "aiPrompt"
    ? "Same as AI tools and code keeps your words and capitals as said."
    : `Same as ${KIND_LABELS[kind]} follows the Style screen.`;
}

export const INSTRUCTIONS_PLACEHOLDER = "e.g. keep it to one line, or bullet my updates";

/** The row's second line: "Formal · Prose · Default cleanup · Email Layout". */
export function profileSummary(profile: ProfileEntry): string {
  const cleanup =
    profile.cleanup === "default" ? "Default cleanup" : profile.cleanup === "verbatim" ? "Word for word" : `${cleanupLabel(profile.cleanup)} cleanup`;
  return [styleLabel(profile.style), FORMATTING_SUMMARY[profile.formatting] ?? "", cleanup, profile.emailLayout ? "Email Layout" : ""]
    .filter((part) => part.length > 0)
    .join(" · ");
}

/** Under the Cleanup choice: what Default means right now. */
export function cleanupNote(defaultCleanup: string | null): string {
  const now = defaultCleanup ? ` (${cleanupLabel(defaultCleanup)})` : "";
  return `Word for word keeps every “um” and repeat. Default follows Settings${now}.`;
}

export const EMAIL_LAYOUT_NOTE = "Lays the text out as an email: greeting, paragraphs, sign-off. On for email apps. Never adds words you didn't say.";

export type ProfileField = "app" | "instructions";

/** Where the error shows, and what it says; null for none. An error with no field is a toast. */
export function profileError(error: ProfileEditError, appName: string): { field: ProfileField | null; message: string } | null {
  switch (error) {
    case "none":
      return null;
    case "appMissing":
      return { field: "app", message: "Choose the app this Profile is for." };
    case "alreadyThere":
      return { field: "app", message: `${appName} already has a Profile. Edit that one instead.` };
    case "full":
      return { field: "app", message: "You have 100 Profiles, the most VoiceCtrl keeps. Delete one to add another." };
    case "toneTooLong":
    case "instructionsTooLong":
      return { field: "instructions", message: "Keep it to 200 characters or fewer." };
    default:
      return { field: null, message: "That couldn't be saved. Try again." };
  }
}

/** The exe under the app's name, as Task Manager's Details tab shows it. */
export function exeName(processName: string): string {
  return processName.toLowerCase().endsWith(".exe") ? processName : `${processName}.exe`;
}
