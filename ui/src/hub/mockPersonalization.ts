/**
 * The Dictionary and Snippets screens' fake host commands (spec §6), for `npm run dev`, Vitest and
 * the Playwright visual tests. The lists live in memory and start from the approved board's
 * sample data; the rules are a small copy of Core's PersonalizationLibrary, enough to reach every
 * state on the board. Core's own tests hold the real rules.
 */
import type {
  ApiKeyState,
  DictionaryEditError,
  DictionarySuggestion,
  DictionaryEntry,
  ExportResult,
  ImportResult,
  Snippet,
  SnippetEditError,
  SuggestionSettingsView,
} from "../bridge/contracts.g";
import type { MockHost } from "../bridge/mock";

export const SAMPLE_WORDS: readonly DictionaryEntry[] = [
  { word: "Bluepeak", alsoHeardAs: ["blue peak"] },
  { word: "WebView2", alsoHeardAs: ["web view two"] },
  { word: "Aadersh", alsoHeardAs: [] },
  { word: "Keizersgracht", alsoHeardAs: ["kaiser gracht"] },
  { word: "NOI", alsoHeardAs: ["noy"] },
];

export const SAMPLE_SNIPPETS: readonly Snippet[] = [
  { trigger: "my address", expansion: "Keizersgracht 123, 1015 CJ Amsterdam" },
  { trigger: "sign off", expansion: "Best regards,\nVedarth" },
  { trigger: "standup link", expansion: "https://meet.example.com/standup" },
  { trigger: "out of office", expansion: "Thanks for your message. I am out until Monday and will reply when I am back." },
];

/** The approved board's suggestions. */
export const SAMPLE_SUGGESTIONS: readonly DictionarySuggestion[] = [
  { word: "Maya Ortiz", count: 6 },
  { word: "Lisbon Ops", count: 4 },
  { word: "Glide", count: 3 },
];

export interface PersonalizationScenario {
  words?: readonly DictionaryEntry[];
  snippets?: readonly Snippet[];
  /** What Import answers: ok (the board's "12 words, 3 already there"), failed, or cancel. */
  importAs?: "ok" | "failed" | "cancel";
  /** "Suggest Dictionary words": on (the default, with the board's three), off, or on with none found. */
  suggestions?: "on" | "off" | "none";
  /** Mode is Offline: no suggestions are made. */
  offline?: boolean;
  typeSafeKey?: ApiKeyState;
}

/** Reads a scenario from the page URL, e.g. `hub.html?words=empty&import=failed`. */
export function personalizationFromQuery(search: string): PersonalizationScenario {
  const q = new URLSearchParams(search);
  const scenario: PersonalizationScenario = {};
  if (q.get("words") === "empty") scenario.words = [];
  if (q.get("snippets") === "empty") scenario.snippets = [];
  const importAs = q.get("import");
  if (importAs === "ok" || importAs === "failed" || importAs === "cancel") scenario.importAs = importAs;
  const suggestions = q.get("sugg");
  if (suggestions === "on" || suggestions === "off" || suggestions === "none") scenario.suggestions = suggestions;
  if (q.get("mode") === "offline") scenario.offline = true;
  const tsKey = q.get("tskey");
  if (tsKey === "none" || tsKey === "saved" || tsKey === "works" || tsKey === "rejected") scenario.typeSafeKey = tsKey;
  return scenario;
}

const same = (a: string, b: string) => a.localeCompare(b, undefined, { sensitivity: "accent" }) === 0;
const phrases = (text: string) => text.split(",").map((p) => p.trim()).filter((p) => p.length > 0);
// Always a new array: the real host answers with fresh JSON, so React must never see the old one mutated.
const insertAt = <T>(list: readonly T[], index: number, item: T): T[] => {
  const at = Math.max(0, Math.min(index, list.length));
  return [...list.slice(0, at), item, ...list.slice(at)];
};

export function installPersonalizationMock(host: MockHost, scenario: PersonalizationScenario = {}): void {
  let words: DictionaryEntry[] = [...(scenario.words ?? SAMPLE_WORDS)];
  let snippets: Snippet[] = [...(scenario.snippets ?? SAMPLE_SNIPPETS)];

  const importResult = (added: number): ImportResult =>
    scenario.importAs === "failed"
      ? { outcome: "failed", added: 0, alreadyThere: 0, notAddedFull: 0, rejectedLines: [] }
      : scenario.importAs === "cancel"
        ? { outcome: "cancelled", added: 0, alreadyThere: 0, notAddedFull: 0, rejectedLines: [] }
        : { outcome: "imported", added, alreadyThere: 3, notAddedFull: 0, rejectedLines: [] };
  const exportResult = (count: number): ExportResult => ({ outcome: "exported", count, folder: "Downloads" });

  host.handle("dictionary.list", () => ({ entries: words }));
  host.handle("dictionary.save", ({ original, word, alsoHeardAs }) => {
    const trimmed = word.trim();
    const at = original === null ? -1 : words.findIndex((e) => same(e.word, original));
    let error: DictionaryEditError = "none";
    if (trimmed.length === 0) error = "wordMissing";
    else if (trimmed.length > 64) error = "wordTooLong";
    else if (trimmed.startsWith("#") || trimmed.includes("|")) error = "wordBadCharacter";
    else if (phrases(alsoHeardAs).length > 5) error = "heardAsTooMany";
    else if (words.some((e, i) => i !== at && same(e.word, trimmed))) error = "alreadyThere";
    if (error === "none") {
      const entry = { word: trimmed, alsoHeardAs: phrases(alsoHeardAs) };
      words = at < 0 ? [entry, ...words] : words.map((e, i) => (i === at ? entry : e));
    }
    return { error, entries: words };
  });
  host.handle("dictionary.remove", ({ word }) => {
    const index = words.findIndex((e) => same(e.word, word));
    if (index >= 0) words = words.filter((_, i) => i !== index);
    return { index, entries: words };
  });
  host.handle("dictionary.restore", ({ entry, index }) => {
    if (!words.some((e) => same(e.word, entry.word))) words = insertAt(words, index, entry);
    return { entries: words };
  });
  host.handle("dictionary.import", () => {
    const result = importResult(12);
    if (result.outcome === "imported") {
      words = [...words, ...["Maya Ortiz", "Lisbon Ops", "Glide"].map((word) => ({ word, alsoHeardAs: [] }))];
    }
    return result;
  });
  host.handle("dictionary.export", () => exportResult(words.length));

  host.handle("snippets.list", () => ({ snippets }));
  host.handle("snippets.save", ({ original, trigger, expansion }) => {
    const trimmed = trigger.trim();
    const at = original === null ? -1 : snippets.findIndex((s) => same(s.trigger, original));
    let error: SnippetEditError = "none";
    if (trimmed.length === 0) error = "triggerMissing";
    else if (trimmed.length > 64) error = "triggerTooLong";
    else if (trimmed.startsWith("#") || trimmed.includes("=")) error = "triggerBadCharacter";
    else if (snippets.some((s, i) => i !== at && same(s.trigger, trimmed))) error = "alreadyThere";
    if (error === "none") {
      const snippet = { trigger: trimmed, expansion: expansion.trim() };
      snippets = at < 0 ? [snippet, ...snippets] : snippets.map((s, i) => (i === at ? snippet : s));
    }
    return { error, snippets };
  });
  host.handle("snippets.remove", ({ trigger }) => {
    const index = snippets.findIndex((s) => same(s.trigger, trigger));
    if (index >= 0) snippets = snippets.filter((_, i) => i !== index);
    return { index, snippets };
  });
  host.handle("snippets.restore", ({ snippet, index }) => {
    if (!snippets.some((s) => same(s.trigger, snippet.trigger))) snippets = insertAt(snippets, index, snippet);
    return { snippets };
  });
  host.handle("snippets.import", () => {
    const result = importResult(5);
    if (result.outcome === "imported") snippets = [...snippets, { trigger: "brb", expansion: "be right back" }];
    return { ...result, alreadyThere: result.outcome === "imported" ? 0 : result.alreadyThere };
  });
  host.handle("snippets.export", () => exportResult(snippets.length));

  // ---- Dictionary Suggestions and the Word suggestions rows
  let suggestions: DictionarySuggestion[] = scenario.suggestions === "none" ? [] : [...SAMPLE_SUGGESTIONS];
  const keyState = scenario.typeSafeKey ?? "none";
  let settings: SuggestionSettingsView = {
    on: scenario.suggestions !== "off",
    keyState,
    keyMask: keyState === "none" ? null : "ts_l ···· 9c2e",
  };
  const saveSettings = (patch: Partial<SuggestionSettingsView>) => {
    settings = { ...settings, ...patch };
    host.emit("settings.suggestions", { suggestions: settings });
  };

  host.handle("dictionary.suggestions", () => ({
    on: settings.on,
    offline: scenario.offline ?? false,
    items: settings.on && !scenario.offline ? suggestions.filter((s) => !words.some((e) => same(e.word, s.word))) : [],
  }));
  host.handle("dictionary.addSuggestion", ({ word }) => {
    suggestions = suggestions.filter((s) => !same(s.word, word));
    if (words.some((e) => same(e.word, word))) return { error: "alreadyThere", entries: words };
    words = [{ word, alsoHeardAs: [] }, ...words];
    return { error: "none", entries: words };
  });
  host.handle("dictionary.ignoreSuggestion", ({ word }) => {
    suggestions = suggestions.filter((s) => !same(s.word, word));
    return null;
  });
  host.handle("settings.getSuggestions", () => settings);
  host.handle("settings.setSuggestWords", ({ on }) => {
    saveSettings({ on });
    return null;
  });
  host.handle("settings.saveTypeSafeKey", async ({ key }) => {
    // Long enough for "Checking the key" to be seen and tested.
    await new Promise((resolve) => setTimeout(resolve, 30));
    const text = key.trim();
    if (text.length < 20) return { check: "didNotWork" };
    saveSettings({ keyState: "works", keyMask: `${text.slice(0, 4)} ···· ${text.slice(-4)}` });
    return { check: "works" };
  });
  host.handle("settings.removeTypeSafeKey", () => {
    saveSettings({ keyState: "none", keyMask: null });
    return null;
  });
}
