/**
 * The Style screen's words (ticket 56), kept apart from the screen so the tests can pin them. The
 * stored names are Core's: Style formal, casual, veryCasual, excited; the kinds of app as AppKind.
 */
import type { AppKind, Style } from "../bridge/contracts.g";
import type { Option } from "../design/components";

export const STYLE_OPTIONS: readonly Option<Style>[] = [
  { value: "formal", label: "Formal" },
  { value: "casual", label: "Casual" },
  { value: "veryCasual", label: "Very casual" },
  { value: "excited", label: "Excited" },
];

export const KIND_LABELS: Record<AppKind, string> = {
  personalChat: "Personal chat",
  workChat: "Work chat",
  email: "Email",
  document: "Documents",
  other: "Other apps",
  aiPrompt: "AI tools and code",
};

/** Under each card's title: what that kind is. */
export const KIND_NOTES: Record<AppKind, string> = {
  personalChat: "Messages to friends and family.",
  workChat: "Messages to colleagues.",
  email: "Laid out as an email: greeting, paragraphs, sign-off.",
  document: "Paragraphs, and a heading on its own line when you say one.",
  other: "Every app not on another card.",
  aiPrompt: "Your words and capitals stay exactly as said, so names in code and commands never change.",
};

export const STYLE_NOTES: Record<Style, string> = {
  formal: "Full sentences, contractions written out.",
  casual: "Capitals, no full stop to end a line.",
  veryCasual: "Starts lowercase. “I” and names keep their capitals.",
  excited: "Exclamation marks to end sentences.",
};

export function styleLabel(style: string): string {
  return STYLE_OPTIONS.find((o) => o.value === style)?.label ?? "";
}

export const YOUR_NAME_NOTE = "Signs the emails VoiceCtrl writes for you. Leave it empty to sign without a name.";

export const YOUR_NAME_ROW = "your-name";
export const MOVE_APPS_ROW = "move-apps";
