/**
 * Sound and Overlay, Privacy and App: fake host commands (spec §6), for `npm run dev`, Vitest and
 * the Playwright visual tests. Update checks walk the real states on a timer: a check finds 4.1
 * unless the scenario says it is up to date.
 */
import type { MockHost } from "../bridge/mock";
import type { GeneralSettingsView, KeepDictations, OverlayDock, UpdateState, UpdateView } from "../bridge/contracts.g";

export const MOCK_NEW_VERSION = "4.1";

export interface GeneralScenario {
  /** The update row's state, held still for screenshots: `downloading:0.62`, or `failed`. */
  update?: { state: UpdateState; progress?: number; failed?: boolean };
  /** Whether a check finds a new version (default true). */
  newVersion?: boolean;
  keep?: KeepDictations;
  /** How many kept Dictations a shorter Keep Dictations would remove. */
  removes?: number;
  soundCues?: boolean;
  dock?: OverlayDock;
  alwaysShow?: boolean;
  startWithWindows?: boolean;
  engineRecord?: boolean;
  /** Copy diagnostics finds the clipboard busy. */
  clipboardBusy?: boolean;
  /** How often a check or download moves on, in ms. */
  stepMs?: number;
}

const STATES: readonly UpdateState[] = ["upToDate", "checking", "downloading", "ready", "off", "available"];
const DOCKS: readonly OverlayDock[] = ["bottom", "left", "right"];
const KEEPS: readonly KeepDictations[] = ["last50", "last24Hours", "none"];

/** Reads a scenario from the page URL, e.g. `hub.html?screen=settings&update=downloading:0.62`. */
export function generalScenarioFromQuery(search: string): GeneralScenario {
  const q = new URLSearchParams(search);
  const scenario: GeneralScenario = {};
  const [state, progress] = (q.get("update") ?? "").split(":");
  if (state === "failed") scenario.update = { state: "upToDate", failed: true };
  else if (STATES.includes(state as UpdateState)) scenario.update = { state: state as UpdateState, progress: Number(progress ?? 0.62) };
  if (q.get("uptodate") === "1") scenario.newVersion = false;
  const keep = q.get("keep");
  if (KEEPS.includes(keep as KeepDictations)) scenario.keep = keep as KeepDictations;
  const removes = q.get("removes");
  if (removes) scenario.removes = Number(removes);
  const dock = q.get("dock");
  if (DOCKS.includes(dock as OverlayDock)) scenario.dock = dock as OverlayDock;
  if (q.get("sound") === "1") scenario.soundCues = true;
  if (q.get("always") === "1") scenario.alwaysShow = true;
  if (q.get("autostart") === "0") scenario.startWithWindows = false;
  if (q.get("engine") === "1") scenario.engineRecord = true;
  if (q.get("clipboardBusy") === "1") scenario.clipboardBusy = true;
  const step = q.get("step");
  if (step) scenario.stepMs = Number(step);
  return scenario;
}

/** Today at 09:41 local time: the board's "Checked today at 9:41". */
function todayAt941(): number {
  const d = new Date();
  return new Date(d.getFullYear(), d.getMonth(), d.getDate(), 9, 41).getTime();
}

export function installGeneralMock(host: MockHost, scenario: GeneralScenario = {}): void {
  const current = host.snapshot.version;
  const state = scenario.update?.state ?? "upToDate";
  const hasVersion = state === "downloading" || state === "ready" || state === "available";
  let updates: UpdateView = {
    state,
    automatic: state !== "off" && state !== "available",
    currentVersion: current,
    newVersion: hasVersion ? MOCK_NEW_VERSION : null,
    progress: state === "downloading" ? (scenario.update?.progress ?? 0.62) : 0,
    lastChecked: todayAt941(),
    checkFailed: scenario.update?.failed ?? false,
  };
  let view: GeneralSettingsView = {
    soundCues: scenario.soundCues ?? false,
    overlayDock: scenario.dock ?? "bottom",
    alwaysShowOverlay: scenario.alwaysShow ?? false,
    keepDictations: scenario.keep ?? "last50",
    startWithWindows: scenario.startWithWindows ?? true,
    updates,
    engineRecord: scenario.engineRecord ?? false,
  };
  const step = scenario.stepMs ?? 400;

  const save = (patch: Partial<GeneralSettingsView>) => {
    view = { ...view, ...patch };
    host.emit("settings.general", { general: view });
  };
  const setUpdates = (patch: Partial<UpdateView>) => {
    updates = { ...updates, ...patch };
    save({ updates });
  };

  // One timer at a time: the check's timeout or the download's interval (clearTimeout stops either).
  let timer: ReturnType<typeof setTimeout> | undefined;
  const stop = () => {
    clearTimeout(timer);
    timer = undefined;
  };

  const download = () => {
    stop();
    setUpdates({ state: "downloading", progress: 0 });
    timer = setInterval(() => {
      const progress = Math.min(1, updates.progress + 0.25);
      if (progress >= 1) {
        stop();
        setUpdates({ state: "ready", progress: 0 });
        return;
      }
      setUpdates({ progress });
    }, step);
  };

  const settled = (): UpdateState => (updates.automatic ? "upToDate" : "off");

  host.handle("settings.getGeneral", () => view);
  host.handle("settings.setSoundCues", ({ on }) => {
    save({ soundCues: on });
    return null;
  });
  host.handle("settings.playSoundCues", () => null);
  host.handle("settings.setOverlayDock", ({ dock }) => {
    save({ overlayDock: dock });
    return null;
  });
  host.handle("settings.setAlwaysShowOverlay", ({ on }) => {
    save({ alwaysShowOverlay: on });
    return null;
  });
  host.handle("settings.previewKeepDictations", ({ keep }) => {
    const order = KEEPS.indexOf(keep) - KEEPS.indexOf(view.keepDictations);
    return { removes: order > 0 ? (scenario.removes ?? 12) : 0 };
  });
  host.handle("settings.setKeepDictations", ({ keep }) => {
    save({ keepDictations: keep });
    return null;
  });
  host.handle("settings.setEngineRecord", ({ on }) => {
    save({ engineRecord: on });
    return null;
  });
  host.handle("settings.openDataFolder", () => null);
  host.handle("settings.copyDiagnostics", () => ({ copied: !scenario.clipboardBusy }));
  host.handle("settings.setStartWithWindows", ({ on }) => {
    save({ startWithWindows: on });
    return null;
  });

  host.handle("updates.check", () => {
    if (updates.state === "checking" || updates.state === "downloading" || updates.state === "ready") return null;
    setUpdates({ state: "checking", checkFailed: false });
    timer = setTimeout(() => {
      const found = scenario.newVersion ?? true;
      updates = { ...updates, lastChecked: Date.now(), newVersion: found ? MOCK_NEW_VERSION : null };
      if (!found) setUpdates({ state: settled() });
      else if (updates.automatic) download();
      else setUpdates({ state: "available" });
    }, step);
    return null;
  });
  host.handle("updates.download", () => {
    if (updates.state === "available") download();
    return null;
  });
  host.handle("updates.setAutomatic", ({ on }) => {
    updates = { ...updates, automatic: on };
    if (!on && updates.state === "downloading") {
      stop();
      updates = { ...updates, newVersion: null, progress: 0 };
    }
    const keeps = updates.state === "ready" || updates.state === "available" || updates.state === "checking";
    setUpdates({ state: keeps ? updates.state : settled() });
    return null;
  });
  host.handle("updates.restart", () => null);
}
