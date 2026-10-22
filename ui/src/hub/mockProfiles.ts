/**
 * The Profiles screen's fake host commands (spec §6), for `npm run dev`, Vitest and the Playwright
 * visual tests. The list lives in memory and starts from the approved board's sample data; the
 * rules are a small copy of Core's ProfileLibrary, enough to reach every state on the board.
 * Core's own tests hold the real rules.
 */
import type { ProfileEditError, ProfileEntry, RunningApp } from "../bridge/contracts.g";
import type { MockHost } from "../bridge/mock";

const profile = (processName: string, appName: string, fields: Partial<ProfileEntry>): ProfileEntry => ({
  processName,
  appName,
  tone: "",
  formatting: "none",
  cleanup: "default",
  emailLayout: false,
  instructions: "",
  style: "",
  kind: "other",
  ...fields,
});

export const SAMPLE_PROFILES: readonly ProfileEntry[] = [
  profile("OUTLOOK", "Outlook", { instructions: "Friendly but professional", formatting: "prose", emailLayout: true, kind: "email" }),
  profile("slack", "Slack", { style: "veryCasual", instructions: "Bullet my status updates", formatting: "prose", cleanup: "light", kind: "workChat" }),
  profile("Code", "Visual Studio Code", { instructions: "Technical, keep terms exact", formatting: "structured", cleanup: "verbatim", kind: "aiPrompt" }),
];

export const SAMPLE_APPS: readonly RunningApp[] = [
  { processName: "chrome", appName: "Google Chrome", formatting: "none", hasProfile: false, emailLayout: false, kind: "other" },
  { processName: "OUTLOOK", appName: "Outlook", formatting: "none", hasProfile: true, emailLayout: true, kind: "email" },
  { processName: "slack", appName: "Slack", formatting: "prose", hasProfile: true, emailLayout: false, kind: "workChat" },
  { processName: "Teams", appName: "Microsoft Teams", formatting: "prose", hasProfile: false, emailLayout: false, kind: "workChat" },
  { processName: "Code", appName: "Visual Studio Code", formatting: "structured", hasProfile: true, emailLayout: false, kind: "aiPrompt" },
  { processName: "WINWORD", appName: "Word", formatting: "paragraphs", hasProfile: false, emailLayout: false, kind: "document" },
];

export interface ProfilesScenario {
  profiles?: readonly ProfileEntry[];
  apps?: readonly RunningApp[];
}

/** Reads a scenario from the page URL, e.g. `hub.html?profiles=empty`. */
export function profilesFromQuery(search: string): ProfilesScenario {
  const q = new URLSearchParams(search);
  return q.get("profiles") === "empty" ? { profiles: [] } : {};
}

const same = (a: string, b: string) => a.localeCompare(b, undefined, { sensitivity: "accent" }) === 0;
const byName = (list: readonly ProfileEntry[]) => [...list].sort((a, b) => a.appName.localeCompare(b.appName));

export function installProfilesMock(host: MockHost, scenario: ProfilesScenario = {}): void {
  let profiles: ProfileEntry[] = byName(scenario.profiles ?? SAMPLE_PROFILES);
  const apps = scenario.apps ?? SAMPLE_APPS;

  const save = (original: string | null, entry: ProfileEntry): ProfileEditError => {
    if (entry.processName.trim().length === 0) return "appMissing";
    if (entry.instructions.trim().length > 200) return "instructionsTooLong";
    const others = profiles.filter((p) => original === null || !same(p.processName, original));
    if (others.some((p) => same(p.processName, entry.processName))) return "alreadyThere";
    profiles = byName([...others, { ...entry, instructions: entry.instructions.trim() }]);
    return "none";
  };

  host.handle("profiles.list", () => ({ profiles }));
  host.handle("profiles.runningApps", () => ({
    apps: apps.map((a) => ({ ...a, hasProfile: profiles.some((p) => same(p.processName, a.processName)) })),
  }));
  host.handle("profiles.save", ({ original, profile: entry }) => ({ error: save(original, entry), profiles }));
  host.handle("profiles.remove", ({ processName }) => {
    profiles = profiles.filter((p) => !same(p.processName, processName));
    return { profiles };
  });
  host.handle("profiles.restore", ({ profile: entry }) => {
    save(null, entry);
    return { profiles };
  });
}
