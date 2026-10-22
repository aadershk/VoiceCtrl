/**
 * The Setup page's fake host commands, for `npm run dev`, Vitest and the Playwright visual tests
 * (spec §6). A scenario fixes what the key check answers and how the download behaves, so every
 * board state can be reached by clicking through, with deterministic numbers.
 */
import type { MockHost } from "../bridge/mock";
import { installSettingsMock, settingsScenarioFromQuery } from "../hub/mockSettings";
import type { ModelDownloadStatus, SetupKeyCheck, SetupSnapshot } from "../bridge/contracts.g";

const MiB = 1024 * 1024;
export const MOCK_TOTAL_BYTES = 700 * MiB;

export interface SetupScenario {
  /** What `setup.checkKey` answers. Default: a key of 20 or more characters works (as on the board). */
  key?: SetupKeyCheck;
  /**
   * run: 0 → done in a few seconds. hold:N: stops moving at N MB (screenshots).
   * fail:N: stops with "The download stopped" at N MB, then Try again runs to the end.
   * done: the model is already installed.
   */
  download?: "run" | `hold:${number}` | `fail:${number}` | "done";
  hotkey?: string;
  startWithWindows?: boolean;
  /** How long the key check takes, in ms. */
  checkDelay?: number;
}

/** Reads a scenario from the page URL, e.g. `setup.html?key=didNotWork&download=fail:322`. */
export function scenarioFromQuery(search: string): SetupScenario {
  const q = new URLSearchParams(search);
  const scenario: SetupScenario = {};
  const key = q.get("key");
  if (key === "works" || key === "didNotWork" || key === "notChecked") scenario.key = key;
  const download = q.get("download");
  if (download && /^(run|done|hold:\d+|fail:\d+)$/.test(download)) {
    scenario.download = download as NonNullable<SetupScenario["download"]>;
  }
  const hotkey = q.get("hotkey");
  if (hotkey) scenario.hotkey = hotkey;
  const delay = q.get("checkDelay");
  if (delay) scenario.checkDelay = Number(delay);
  return scenario;
}

export function installSetupMock(host: MockHost, scenario: SetupScenario = {}): void {
  const download = scenario.download ?? "run";
  const [mode, mbText] = download.split(":");
  const stopAt = mbText ? Number(mbText) * MiB : null;
  let failedOnce = false;
  let timer: ReturnType<typeof setInterval> | undefined;

  let status: ModelDownloadStatus =
    download === "done"
      ? { state: "done", bytes: MOCK_TOTAL_BYTES, totalBytes: MOCK_TOTAL_BYTES, bytesPerSecond: 0 }
      : { state: "idle", bytes: 0, totalBytes: MOCK_TOTAL_BYTES, bytesPerSecond: 0 };

  const set = (next: ModelDownloadStatus) => {
    status = next;
    host.emit("setup.model", { status });
  };
  const stop = () => clearInterval(timer);

  const snapshot: SetupSnapshot = {
    hotkey: scenario.hotkey ?? "Ctrl",
    startWithWindows: scenario.startWithWindows ?? true,
    model: status,
  };

  // The hotkey step changes keys through the same host commands Settings uses.
  installSettingsMock(host, settingsScenarioFromQuery(typeof location === "undefined" ? "" : location.search));

  host.handle("setup.state", () => ({ ...snapshot, model: status }));

  host.handle("setup.checkKey", async ({ key }) => {
    await new Promise((resolve) => setTimeout(resolve, scenario.checkDelay ?? 900));
    return { check: scenario.key ?? (key.trim().length >= 20 ? "works" : "didNotWork") };
  });

  host.handle("setup.downloadModel", () => {
    if (status.state === "downloading" || status.state === "done") return null;
    // 10 MiB/s, so "312 of 700 MB" reads "about 40 s left".
    const rate = 10 * MiB;
    const holding = mode === "hold" || (mode === "fail" && !failedOnce);
    if (holding && stopAt !== null) {
      set({ ...status, state: "downloading", bytes: stopAt, bytesPerSecond: rate });
      if (mode === "fail") {
        failedOnce = true;
        setTimeout(() => set({ ...status, state: "failed", bytesPerSecond: 0 }), 0);
      }
      return null;
    }
    set({ ...status, state: "downloading", bytesPerSecond: rate });
    stop();
    timer = setInterval(() => {
      const bytes = Math.min(MOCK_TOTAL_BYTES, status.bytes + 12 * MiB);
      if (bytes >= MOCK_TOTAL_BYTES) {
        stop();
        set({ state: "done", bytes: MOCK_TOTAL_BYTES, totalBytes: MOCK_TOTAL_BYTES, bytesPerSecond: 0 });
      } else {
        set({ ...status, bytes });
      }
    }, 100);
    return null;
  });

  host.handle("setup.stopDownload", () => {
    stop();
    if (status.state === "downloading") set({ ...status, state: "idle", bytesPerSecond: 0 });
    return null;
  });

  host.handle("setup.finish", () => null);
}
