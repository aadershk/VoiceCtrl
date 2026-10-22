/**
 * The Hub's six screens and every named Settings row (research 10 "Structure", DESIGN §12).
 * One list feeds the sidebar, Quick Find and the Settings page, so a row can't be findable
 * without existing, or exist without being findable.
 */
import type { IconName } from "../design/icons";

export type ScreenId = "dictations" | "dictionary" | "snippets" | "style" | "profiles" | "settings";

export interface Screen {
  id: ScreenId;
  label: string;
  icon: IconName;
  /** The one-line subtitle under the page title. */
  subtitle: string;
}

export const screens: readonly Screen[] = [
  { id: "dictations", label: "Dictations", icon: "dictations", subtitle: "Your last 50, kept on this PC. Audio is never saved." },
  { id: "dictionary", label: "Dictionary", icon: "dictionary", subtitle: "Names, jargon and spellings VoiceCtrl should always get right." },
  { id: "snippets", label: "Snippets", icon: "snippets", subtitle: "Say a short trigger and VoiceCtrl types the full text." },
  { id: "style", label: "Style", icon: "style", subtitle: "How your words read in each kind of app. A Profile can set one app differently." },
  { id: "profiles", label: "Profiles", icon: "profiles", subtitle: "Per-app rules for how your Transcriptions read. Apps without one use the defaults." },
  { id: "settings", label: "Settings", icon: "settings", subtitle: "How VoiceCtrl listens, transcribes and looks. Changes apply straight away." },
];

export function isScreenId(value: string): value is ScreenId {
  return screens.some((s) => s.id === value);
}

/**
 * Where the Hub opens when the host asks for a place other than Dictations (the Overlay's "Add
 * an API key" opens Settings at the key row): `hub.html?screen=settings&row=api-key`. Anything
 * unknown opens Dictations as usual.
 */
export function startTargetFromQuery(search: string): { screen: ScreenId; row?: string } | null {
  const query = new URLSearchParams(search);
  const screen = query.get("screen");
  if (!screen || !isScreenId(screen)) return null;
  const row = query.get("row");
  return row ? { screen, row } : { screen };
}

export function screenById(id: ScreenId): Screen {
  const screen = screens.find((s) => s.id === id);
  if (!screen) throw new Error(`Unknown screen ${id}`);
  return screen;
}

export type SettingsGroup = "Dictation" | "Transcription" | "Word suggestions" | "Sound and Overlay" | "Privacy" | "App";

export const settingsGroups: readonly SettingsGroup[] = [
  "Dictation",
  "Transcription",
  "Word suggestions",
  "Sound and Overlay",
  "Privacy",
  "App",
];

export interface SettingsRowInfo {
  /** Stable id: Quick Find lands on `rowDomId(id)`, and each Settings slice builds its row under it. */
  id: string;
  group: SettingsGroup;
  label: string;
  description: string;
  /** Other words people search with (research 10: aliases fix misses in code). */
  aliases?: readonly string[];
  /** False for a row shown only in some states (Other's address), which Quick Find can't land on. */
  findable?: false;
}

export const settingsRows: readonly SettingsRowInfo[] = [
  { id: "microphone", group: "Dictation", label: "Microphone", description: "Used for every Dictation. If it is unplugged, VoiceCtrl uses the Windows default.", aliases: ["mic", "input device"] },
  { id: "hotkey", group: "Dictation", label: "Hotkey", description: "Double-tap it to show or hide the Overlay.", aliases: ["shortcut", "double-tap"] },
  { id: "trigger-key", group: "Dictation", label: "Trigger Key", description: "Hold to talk and release to paste. Tap twice quickly to keep recording hands-free.", aliases: ["push to talk", "hold to talk"] },
  { id: "rewrite-key", group: "Dictation", label: "Rewrite key", description: "Hold, say what to change (“make this shorter”, “turn this into bullets”) and release. It rewrites the selected text, or your last Dictation if nothing is selected.", aliases: ["rewrite", "edit by voice", "command", "translate", "second hotkey"] },
  { id: "paste-again", group: "Dictation", label: "Paste Again", description: "Pastes your latest Transcription once more." },
  { id: "press-enter", group: "Dictation", label: "Press Enter", description: "End a Dictation with “press enter” to send it, e.g. in a chat.", aliases: ["send"] },
  { id: "mode", group: "Transcription", label: "Mode", description: "Auto uses Online when you are connected and Offline when you are not.", aliases: ["online", "offline", "auto"] },
  { id: "online-provider", group: "Transcription", label: "Online provider", description: "Online sends your audio and text only to the provider you pick. If it is busy, VoiceCtrl tries your other saved keys, then Offline.", aliases: ["gemini", "nvidia", "openai", "google", "groq", "openrouter", "ai provider"] },
  { id: "other-provider", group: "Transcription", label: "Address and model", description: "Any service that speaks OpenAI's chat API, like Groq or OpenRouter, with a model it offers.", findable: false },
  { id: "api-key", group: "Transcription", label: "API key", description: "Needed for Online. Stored on this PC only.", aliases: ["gemini key", "nvidia key", "openai key"] },
  { id: "default-cleanup", group: "Transcription", label: "Default cleanup", description: "How much of the “um”s and repeats to remove. A Profile can choose differently for one app.", aliases: ["word for word", "verbatim", "filler words"] },
  { id: "smart-cleanup", group: "Transcription", label: "Smart cleanup", description: "When you dictate offline, a model on this PC removes the “um”s, follows corrections and sets out lists. Online already does this. Tested in English only; in other languages Offline may keep its plain cleanup.", aliases: ["rewrite", "local model", "ai cleanup", "formatting", "lists"] },
  { id: "smart-cleanup-model", group: "Transcription", label: "Smart cleanup model", description: "Downloaded once and kept on this PC. Better writes closer to Online; Standard is quicker.", aliases: ["language model", "download", "qwen"] },
  { id: "offline-models", group: "Transcription", label: "Offline models", description: "English is installed. A language pack adds more languages.", aliases: ["languages", "language pack"] },
  { id: "suggest-words", group: "Word suggestions", label: "Suggest Dictionary words", description: "Spots names and words you say often, for you to add. Never used in Offline mode.", aliases: ["suggestions"] },
  { id: "typesafe-key", group: "Word suggestions", label: "TypeSafe key", description: "Optional, for sharper suggestions. Sends recent Dictation text to TypeSafe." },
  { id: "sound-cues", group: "Sound and Overlay", label: "Sound Cues", description: "Soft tones when a Dictation starts, stops and pastes.", aliases: ["sounds"] },
  { id: "mute-other-audio", group: "Sound and Overlay", label: "Mute Other Audio", description: "Lowers other apps while you dictate, then turns them back up.", aliases: ["music", "volume"] },
  { id: "overlay-position", group: "Sound and Overlay", label: "Overlay position", description: "Where the Overlay sits. You can also drag it.", aliases: ["dock", "bar position"] },
  { id: "always-show-overlay", group: "Sound and Overlay", label: "Always show the Overlay", description: "Keeps a small, quiet VoiceCtrl mark on screen to click. Off: it appears only when you dictate.", aliases: ["handle"] },
  { id: "keep-dictations", group: "Privacy", label: "Keep Dictations", description: "Text only, on this PC. Audio is never saved to disk. A recording you cancel after 3 seconds is held in memory for 10 minutes so you can get its text back; None keeps nothing.", aliases: ["history", "retention", "cancelled recording"] },
  { id: "your-data", group: "Privacy", label: "Your data", description: "Dictionary, Snippets, Profiles and settings, as files.", aliases: ["data folder", "files"] },
  { id: "copy-diagnostics", group: "Privacy", label: "Copy diagnostics", description: "For reporting a problem on GitHub. Copies a log without your Dictations, keys or file names.", aliases: ["report a problem", "log", "bug"] },
  { id: "engine-record", group: "Privacy", label: "Engine record", description: "Trial helper. Notes, for each Dictation, which words the engine fixed and how long it took, never what you said. Stays on this PC and is added to Copy diagnostics while on. Turning it off deletes it.", aliases: ["trial", "engine log", "timings"] },
  { id: "start-with-windows", group: "App", label: "Start VoiceCtrl with Windows", description: "Starts quietly by the clock when you sign in.", aliases: ["autostart", "startup"] },
  { id: "theme", group: "App", label: "Theme", description: "System follows Windows.", aliases: ["dark mode", "light mode", "appearance"] },
  { id: "automatic-updates", group: "App", label: "Automatic updates", description: "New versions download quietly and install the next time VoiceCtrl starts.", aliases: ["updates", "version"] },
];

/** The element id a Quick Find landing scrolls to and flashes. */
export function rowDomId(row: string): string {
  return `find-${row}`;
}
