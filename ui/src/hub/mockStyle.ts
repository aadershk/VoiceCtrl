/**
 * The Style screen's fake host commands (ticket 56), for `npm run dev`, Vitest and the Playwright
 * visual tests. The examples are Core's StyleSettingsService output, copied; Core's own tests hold
 * the real rules.
 */
import type { AppKind, Style, StyleExample, StyleGroup, StyleView } from "../bridge/contracts.g";
import type { MockHost } from "../bridge/mock";

const GROUPS: readonly AppKind[] = ["personalChat", "workChat", "email", "document", "other", "aiPrompt"];

const KNOWN_APPS: Record<AppKind, readonly string[]> = {
  personalChat: ["WhatsApp", "Telegram", "Signal", "Messenger", "Discord", "Skype", "Viber"],
  workChat: ["Slack", "Microsoft Teams", "Google Chat", "Element"],
  email: ["Outlook", "Gmail", "Thunderbird", "Mailspring", "eM Client", "Mailbird"],
  document: ["Word", "Google Docs", "Notion", "OneNote", "Obsidian", "WordPad", "LibreOffice", "Scrivener"],
  other: [],
  aiPrompt: ["ChatGPT", "Claude", "Gemini", "Cursor", "VS Code", "Windsurf", "Zed", "Visual Studio", "JetBrains IDEs", "Terminals"],
};

const EXAMPLES: Record<AppKind, readonly [string, string, string, string]> = {
  personalChat: [
    "Yes, sounds good. I will be there in five minutes.",
    "Yeah, sounds good. I'll be there in five minutes",
    "yeah, sounds good. I'll be there in five minutes",
    "Yeah, sounds good! I'll be there in five minutes!",
  ],
  workChat: [
    "I have pushed the fix. Can you take a look when you are free?",
    "I've pushed the fix. Can you take a look when you're free?",
    "I've pushed the fix. can you take a look when you're free?",
    "I've pushed the fix! Can you take a look when you're free?",
  ],
  email: [
    "Hi Sam,\n\nThanks for sending this over. I will read it tonight and get back to you tomorrow.\n\nBest,\nAlex",
    "Hi Sam,\n\nThanks for sending this over. I'll read it tonight and get back to you tomorrow\n\nBest,\nAlex",
    "hi Sam,\n\nthanks for sending this over. I'll read it tonight and get back to you tomorrow\n\nbest,\nAlex",
    "Hi Sam,\n\nThanks for sending this over! I'll read it tonight and get back to you tomorrow!\n\nBest,\nAlex",
  ],
  document: [
    "We are launching in March. It is a tight timeline, but we are ready.",
    "We're launching in March. It's a tight timeline, but we're ready",
    "we're launching in March. it's a tight timeline, but we're ready",
    "We're launching in March! It's a tight timeline, but we're ready!",
  ],
  other: [
    "Yes, I will send the file tonight. Let me know if it does not open.",
    "Yeah, I'll send the file tonight. Let me know if it doesn't open",
    "yeah, I'll send the file tonight. let me know if it doesn't open",
    "Yeah, I'll send the file tonight! Let me know if it doesn't open!",
  ],
  aiPrompt: ["Don't rename the API client. Just fix the retry logic.", "", "", ""],
};

const STYLES: readonly Style[] = ["formal", "casual", "veryCasual", "excited"];

/** The order Core lists its apps in (AppKinds.Known). */
const CORE_ORDER: readonly AppKind[] = ["email", "workChat", "personalChat", "document", "aiPrompt"];

export interface StyleScenario {
  styles?: Partial<Record<AppKind, Style>>;
  moved?: Record<string, AppKind>;
  yourName?: string;
}

/** Reads a scenario from the page URL, e.g. `hub.html?style=moved`. */
export function styleFromQuery(search: string): StyleScenario {
  const q = new URLSearchParams(search);
  return q.get("style") === "moved" ? { moved: { Discord: "workChat", mattermost: "workChat" }, yourName: "Alex" } : {};
}

const DEFAULTS: Record<AppKind, Style | null> = {
  personalChat: "casual",
  workChat: "casual",
  email: "formal",
  document: "formal",
  other: "formal",
  aiPrompt: null,
};

const ownKind = (app: string): AppKind | null =>
  GROUPS.find((kind) => KNOWN_APPS[kind].some((name) => name.toLowerCase() === app.toLowerCase())) ?? null;

export function installStyleMock(host: MockHost, scenario: StyleScenario = {}): void {
  const styles: Record<AppKind, Style | null> = { ...DEFAULTS, ...scenario.styles };
  let moved: Record<string, AppKind> = { ...scenario.moved };
  let yourName = scenario.yourName ?? "";

  const examples = (kind: AppKind): StyleExample[] =>
    kind === "aiPrompt"
      ? [{ style: null, text: EXAMPLES.aiPrompt[0] }]
      : STYLES.map((style, i) => ({ style, text: EXAMPLES[kind][i] ?? "" }));

  const view = (): StyleView => ({
    groups: GROUPS.map((kind): StyleGroup => {
      const known = CORE_ORDER.flatMap((k) => KNOWN_APPS[k].map((name) => ({ name, own: k })))
        .filter(({ name, own }) => (moved[name] ?? own) === kind)
        .map(({ name, own }) => ({ id: name, name, moved: own !== kind }));
      const added = Object.entries(moved)
        .filter(([name, k]) => k === kind && ownKind(name) === null)
        .map(([name]) => ({ id: name, name: name.charAt(0).toUpperCase() + name.slice(1), moved: true }));
      return { kind, style: styles[kind], apps: [...known, ...added], examples: examples(kind) };
    }),
    yourName,
  });

  host.handle("style.get", () => view());
  host.handle("style.set", ({ kind, style }) => {
    if (kind !== "aiPrompt") styles[kind] = style;
    return view();
  });
  host.handle("style.setName", ({ name }) => {
    yourName = name.trim().replace(/\s+/g, " ").slice(0, 60);
    return view();
  });
  host.handle("style.moveApp", ({ app, kind }) => {
    const known = KNOWN_APPS[ownKind(app) ?? "other"].find((name) => name.toLowerCase() === app.toLowerCase());
    const key = known ?? app;
    const rest = Object.fromEntries(Object.entries(moved).filter(([name]) => name.toLowerCase() !== key.toLowerCase()));
    moved = kind === (ownKind(key) ?? "other") ? rest : { ...rest, [key]: kind };
    return view();
  });
}
