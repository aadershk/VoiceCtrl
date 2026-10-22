/**
 * The Dictations screen's fake host commands (spec §6), for `npm run dev`, Vitest and the
 * Playwright visual tests. The sample Dictations are the approved board's (research 10), placed
 * relative to the page's clock, so a fixed clock of 09:50 gives the board's times exactly.
 */
import type { MockHost } from "../bridge/mock";
import type { DictationItem, DictationsView, FailedDictationItem, KeepDictations } from "../bridge/contracts.g";

const MIN = 60_000;

export interface DictationsScenario {
  /** "empty": nothing kept yet (first run). */
  dictations?: "some" | "empty";
  keep?: KeepDictations;
  /** How many Failed Dictations are kept; the board shows one. */
  failed?: number;
  /** The first kept entry is a cancelled recording (VED-76), not a failure. */
  cancelled?: boolean;
  /** The first Failed Dictation is being retried. */
  retrying?: boolean;
  /** No offline model, so no "Retry offline". */
  noOfflineModel?: boolean;
  /** Retries fail again. */
  retryFails?: boolean;
  /** Smart cleanup edited the newest two, so they offer "Restore original". */
  original?: boolean;
  /** How the host answers Paste Again. */
  paste?: "pasted" | "copied" | "failed" | "noWindow" | "busy";
}

/** Reads a scenario from the page URL, e.g. `hub.html?dictations=empty&failed=2&keep=none`. */
export function dictationsScenarioFromQuery(search: string): DictationsScenario {
  const q = new URLSearchParams(search);
  const scenario: DictationsScenario = {};
  if (q.get("dictations") === "empty") scenario.dictations = "empty";
  const keep = q.get("keep");
  if (keep === "last50" || keep === "last24Hours" || keep === "none") scenario.keep = keep;
  const failed = q.get("failed");
  if (failed) scenario.failed = Number(failed);
  if (q.get("retrying") === "1") scenario.retrying = true;
  if (q.get("cancelled") === "1") scenario.cancelled = true;
  if (q.get("offlineModel") === "0") scenario.noOfflineModel = true;
  if (q.get("retryFails") === "1") scenario.retryFails = true;
  if (q.get("original") === "1") scenario.original = true;
  const paste = q.get("paste");
  if (paste === "pasted" || paste === "copied" || paste === "failed" || paste === "noWindow" || paste === "busy") scenario.paste = paste;
  return scenario;
}

/** The board's Dictations, newest first, around `now`. */
export function sampleDictations(now: number): DictationItem[] {
  const day = new Date(now);
  const yesterdayAt = (h: number, m: number) => new Date(day.getFullYear(), day.getMonth(), day.getDate() - 1, h, m).getTime();
  return [
    {
      id: "d-outlook",
      text: "Hi Maya, thanks for sending the draft over. I've gone through sections two and three and left comments on the forecasting part. Can we find thirty minutes on Thursday?",
      atMs: now - 2 * MIN,
      app: "Outlook",
      hasOriginal: false,
    },
    { id: "d-notion", text: "Next step is to compare the load factor on the Amsterdam to Lisbon route before and after the schedule change.", atMs: now - 38 * MIN, app: "Notion", hasOriginal: false },
    { id: "d-chrome", text: "Best noise cancelling headphones under two hundred euros", atMs: now - 63 * MIN, app: "Chrome", hasOriginal: false },
    { id: "d-teams", text: "Can you move our one-to-one to Friday morning? Thursday is packed.", atMs: now - 80 * MIN, app: "Teams", hasOriginal: false },
    { id: "d-whatsapp", text: "Running ten minutes late, save me a seat.", atMs: yesterdayAt(18, 5), app: "WhatsApp", hasOriginal: false },
    { id: "d-vscode", text: "Add a docstring explaining why we clip the forecast at zero.", atMs: yesterdayAt(16, 30), app: "VS Code", hasOriginal: false },
    { id: "d-word", text: "The results suggest that the effect is strongest on routes with a single competitor.", atMs: yesterdayAt(11, 2), app: "Word", hasOriginal: false },
  ];
}

function sampleFailed(now: number, count: number, retrying: boolean, cancelled: boolean): FailedDictationItem[] {
  const all: FailedDictationItem[] = [
    { id: "f-slack", reason: cancelled ? "cancelled" : "noInternet", atMs: now - 9 * MIN, app: "Slack", audioSeconds: 12, retrying },
    { id: "f-mail", reason: "timedOut", atMs: now - 21 * MIN, app: "Mail", audioSeconds: 34, retrying: false },
  ];
  return all.slice(0, count);
}

const RECOVERED = "Pushed the fix for the export bug, can someone give it a quick look before the release?";

export function installDictationsMock(host: MockHost, scenario: DictationsScenario = {}): void {
  const now = Date.now();
  let view: DictationsView = {
    keep: scenario.keep ?? "last50",
    dictations:
      scenario.dictations === "empty" || scenario.keep === "none"
        ? []
        : sampleDictations(now).map((d, i) => ({ ...d, hasOriginal: scenario.original === true && i < 2 })),
    failed: sampleFailed(now, scenario.failed ?? 1, scenario.retrying ?? false, scenario.cancelled ?? false),
    canRetryOffline: !scenario.noOfflineModel,
    pasteAgainKeys: ["Ctrl", "Shift", "V"],
  };
  const deleted = new Map<string, DictationItem>();
  const update = (patch: Partial<DictationsView>) => {
    view = { ...view, ...patch };
    host.emit("dictations.changed", { dictations: view });
  };

  host.handle("dictations.get", () => view);
  host.handle("dictations.restoreOriginal", ({ id }) => {
    update({ dictations: view.dictations.map((d) => (d.id === id ? { ...d, hasOriginal: false } : d)) });
    return null;
  });

  host.handle("dictations.pasteAgain", ({ id }) => {
    const item = id === null ? view.dictations[0] : view.dictations.find((d) => d.id === id);
    const outcome = scenario.paste ?? "pasted";
    return { outcome, app: outcome === "noWindow" || outcome === "busy" ? null : (item?.app ?? null) };
  });

  host.handle("dictations.copy", () => null);
  host.handle("dictations.copyAll", () => ({ count: view.dictations.length }));

  host.handle("dictations.delete", ({ id }) => {
    const item = view.dictations.find((d) => d.id === id);
    if (item) {
      deleted.set(id, item);
      update({ dictations: view.dictations.filter((d) => d.id !== id) });
    }
    return null;
  });

  host.handle("dictations.undoDelete", ({ id }) => {
    const item = deleted.get(id);
    if (item) {
      deleted.delete(id);
      update({ dictations: [...view.dictations, item].sort((a, b) => b.atMs - a.atMs) });
    }
    return null;
  });

  host.handle("dictations.clearAll", () => {
    deleted.clear();
    update({ dictations: [] });
    return null;
  });

  host.handle("dictations.dismiss", ({ id }) => {
    update({ failed: view.failed.filter((f) => f.id !== id) });
    return null;
  });

  host.handle("dictations.retry", ({ id }) => {
    const failed = view.failed.find((f) => f.id === id);
    if (!failed || failed.retrying) return { outcome: "ignored" as const };
    update({ failed: view.failed.map((f) => (f.id === id ? { ...f, retrying: true } : f)) });
    return new Promise((resolve) => {
      setTimeout(() => {
        if (scenario.retryFails) {
          update({ failed: view.failed.map((f) => (f.id === id ? { ...f, retrying: false } : f)) });
          resolve({ outcome: "failed" });
          return;
        }
        const recovered: DictationItem = { id: `d-${id}`, text: RECOVERED, atMs: Date.now(), app: failed.app, hasOriginal: false };
        update({
          failed: view.failed.filter((f) => f.id !== id),
          dictations: view.keep === "none" ? view.dictations : [recovered, ...view.dictations],
        });
        resolve({ outcome: "transcribed" });
      }, 1400);
    });
  });
}
