/**
 * The Settings page's Transcription group: fake host commands (spec §6), for `npm run dev`, Vitest
 * and the Playwright visual tests. The pack sizes are the measured downloads (ticket 22). A key of
 * 20 or more characters "works", as in the Setup mock; one starting "offline" can't be checked.
 */
import type { MockHost } from "../bridge/mock";
import type {
  ApiKeyState,
  ModelDownloadStatus,
  OfflinePack,
  OfflinePackInfo,
  OnlineProvider,
  SmartCleanupPackInfo,
  SmartCleanupTier,
  TranscriptionModePreference,
  TranscriptionSettingsView,
} from "../bridge/contracts.g";

export const MOCK_PACKS: readonly OfflinePackInfo[] = [
  { pack: "european", bytes: 670_478_772, languages: 25, slower: false },
  { pack: "allLanguages", bytes: 1_036_613_791, languages: 99, slower: true },
];

export const MOCK_SMART_PACKS: readonly SmartCleanupPackInfo[] = [
  { tier: "standard", bytes: 1_126_808_390 },
  { tier: "better", bytes: 2_516_680_038 },
];

export interface TranscriptionScenario {
  key?: ApiKeyState;
  /** The chosen Online provider (ticket 60); Gemini by default. */
  provider?: OnlineProvider;
  mode?: TranscriptionModePreference;
  cleanup?: string;
  /** The installed extra pack. */
  pack?: OfflinePack;
  /** A pack part-way down, held still for screenshots: `european:0.4`. */
  download?: { pack: OfflinePack; fraction: number; failed?: boolean };
  /** Smart cleanup: the switch, the installed model, and a model part-way down. */
  smart?: { on?: boolean; installed?: SmartCleanupTier | null; download?: { tier: SmartCleanupTier; fraction: number; failed?: boolean } };
  /** How often a picked pack's fake download advances, in ms. */
  stepMs?: number;
}

const PACKS: readonly OfflinePack[] = ["none", "european", "allLanguages"];

/** Reads a scenario from the page URL, e.g. `hub.html?screen=settings&key=rejected&dl=european:0.4`. */
export function transcriptionScenarioFromQuery(search: string): TranscriptionScenario {
  const q = new URLSearchParams(search);
  const scenario: TranscriptionScenario = {};
  const key = q.get("key");
  if (key === "none" || key === "saved" || key === "works" || key === "rejected") scenario.key = key;
  const provider = q.get("provider");
  if (provider === "gemini" || provider === "nvidia" || provider === "openAi" || provider === "other") scenario.provider = provider;
  const mode = q.get("mode");
  if (mode === "auto" || mode === "online" || mode === "offline") scenario.mode = mode;
  const cleanup = q.get("cleanup");
  if (cleanup) scenario.cleanup = cleanup;
  const pack = q.get("pack");
  if (pack && PACKS.includes(pack as OfflinePack)) scenario.pack = pack as OfflinePack;
  for (const [name, failed] of [["dl", false], ["dlFailed", true]] as const) {
    const [downloadPack, fraction] = (q.get(name) ?? "").split(":");
    if (downloadPack && PACKS.includes(downloadPack as OfflinePack)) {
      scenario.download = { pack: downloadPack as OfflinePack, fraction: Number(fraction ?? 0.4), failed };
    }
  }
  const smart = q.get("smart");
  if (smart === "off") scenario.smart = { on: false, installed: null };
  else if (smart === "standard" || smart === "better") scenario.smart = { installed: smart };
  for (const [name, failed] of [["smartDl", false], ["smartDlFailed", true]] as const) {
    const [tier, fraction] = (q.get(name) ?? "").split(":");
    if (tier === "standard" || tier === "better") {
      scenario.smart = { installed: null, download: { tier, fraction: Number(fraction ?? 0.4), failed } };
    }
  }
  const step = q.get("step");
  if (step) scenario.stepMs = Number(step);
  return scenario;
}

const maskOf = (key: string) => (key.length >= 16 ? `${key.slice(0, 4)} ···· ${key.slice(-4)}` : "····");

function downloadStatus(pack: OfflinePack, fraction: number, failed = false): ModelDownloadStatus {
  const total = MOCK_PACKS.find((p) => p.pack === pack)?.bytes ?? 0;
  // Rounded up, so a fixture at 0.4 reads 40% (the page floors the percentage).
  return { state: failed ? "failed" : "downloading", bytes: Math.ceil(total * fraction), totalBytes: total, bytesPerSecond: failed ? 0 : 6_000_000 };
}

function smartDownloadStatus(tier: SmartCleanupTier, fraction: number, failed = false): ModelDownloadStatus {
  const total = MOCK_SMART_PACKS.find((p) => p.tier === tier)?.bytes ?? 0;
  return { state: failed ? "failed" : "downloading", bytes: Math.ceil(total * fraction), totalBytes: total, bytesPerSecond: failed ? 0 : 12_000_000 };
}

/** A saved key's mask for each provider, as the host would show it. */
const MOCK_MASKS: Record<OnlineProvider, string> = {
  gemini: "AIza ···· 3f9a",
  nvidia: "nvap ···· 8dE2",
  openAi: "sk-p ···· 41xQ",
  other: "gsk_ ···· 7m2P",
};

export function installTranscriptionMock(host: MockHost, scenario: TranscriptionScenario = {}): void {
  const keyState = scenario.key ?? "works";
  const provider = scenario.provider ?? "gemini";
  // Each provider keeps its own key: the chosen one's state is the scenario's, the others have none.
  const keys: Record<OnlineProvider, { state: ApiKeyState; mask: string | null }> = {
    gemini: { state: "none", mask: null },
    nvidia: { state: "none", mask: null },
    openAi: { state: "none", mask: null },
    other: { state: "none", mask: null },
  };
  keys[provider] = { state: keyState, mask: keyState === "none" ? null : MOCK_MASKS[provider] };
  const savedKeys = () => (Object.keys(keys) as OnlineProvider[]).filter((p) => keys[p].state !== "none");
  let view: TranscriptionSettingsView = {
    mode: scenario.mode ?? "auto",
    defaultCleanup: scenario.cleanup ?? "standard",
    provider,
    keyState,
    keyMask: keys[provider].mask,
    other: provider === "other"
      ? { baseUrl: "https://api.groq.com/openai/v1", model: "whisper-large-v3-turbo", audio: "none", textModel: "" }
      : { baseUrl: "", model: "", audio: "unknown", textModel: "" },
    savedKeys: savedKeys(),
    installedPack: scenario.pack ?? "none",
    downloadingPack: scenario.download?.pack ?? null,
    download: scenario.download ? downloadStatus(scenario.download.pack, scenario.download.fraction, scenario.download.failed) : null,
    packs: [...MOCK_PACKS],
    smartCleanup: scenario.smart?.on ?? true,
    smartCleanupTier: scenario.smart?.download?.tier ?? scenario.smart?.installed ?? "better",
    smartCleanupInstalled: scenario.smart?.installed ?? null,
    smartCleanupDownloading: scenario.smart?.download?.tier ?? null,
    smartCleanupDownload: scenario.smart?.download
      ? smartDownloadStatus(scenario.smart.download.tier, scenario.smart.download.fraction, scenario.smart.download.failed)
      : null,
    smartCleanupPacks: [...MOCK_SMART_PACKS],
  };
  const save = (patch: Partial<TranscriptionSettingsView>) => {
    view = { ...view, ...patch };
    host.emit("settings.transcription", { transcription: view });
  };

  let timer: ReturnType<typeof setInterval> | undefined;
  const stopDownload = () => {
    clearInterval(timer);
    timer = undefined;
  };

  host.handle("settings.getTranscription", () => view);
  host.handle("settings.setMode", ({ mode }) => {
    save({ mode });
    return null;
  });
  host.handle("settings.setDefaultCleanup", ({ level }) => {
    save({ defaultCleanup: level });
    return null;
  });

  const showKey = (patch: Partial<TranscriptionSettingsView> = {}) => {
    const chosen = patch.provider ?? view.provider;
    save({ ...patch, keyState: keys[chosen].state, keyMask: keys[chosen].mask, savedKeys: savedKeys() });
  };

  host.handle("settings.setProvider", ({ provider: next }) => {
    showKey({ provider: next });
    return null;
  });
  host.handle("settings.saveOnlineKey", async ({ key }) => {
    // Long enough for "Checking the key" to be seen and tested.
    await new Promise((resolve) => setTimeout(resolve, 30));
    const text = key.trim();
    if (text.length < 20) return { check: "didNotWork" };
    const works = !text.startsWith("offline");
    keys[view.provider] = { state: works ? "works" : "saved", mask: maskOf(text) };
    showKey();
    return { check: works ? "works" : "notChecked" };
  });
  host.handle("settings.removeOnlineKey", () => {
    keys[view.provider] = { state: "none", mask: null };
    showKey();
    return null;
  });
  host.handle("settings.setOtherProvider", ({ baseUrl, model, textModel }) => {
    if (!/^https?:\/\//.test(baseUrl.trim())) throw new Error("The address must start with https://");
    save({ other: { baseUrl: baseUrl.trim(), model: model.trim(), textModel: textModel.trim(), audio: "unknown" } });
    return { check: keys.other.state === "none" ? null : "works" };
  });

  host.handle("settings.pickOfflinePack", ({ pack }) => {
    stopDownload();
    if (pack === view.installedPack) {
      save({ downloadingPack: null, download: null });
      return null;
    }
    if (pack === "none") {
      save({ installedPack: "none", downloadingPack: null, download: null });
      return null;
    }
    let fraction = 0;
    save({ downloadingPack: pack, download: downloadStatus(pack, 0) });
    timer = setInterval(() => {
      fraction += 0.1;
      if (fraction >= 1) {
        stopDownload();
        save({ installedPack: pack, downloadingPack: null, download: null });
        return;
      }
      save({ download: downloadStatus(pack, fraction) });
    }, scenario.stepMs ?? 300);
    return null;
  });
  let smartTimer: ReturnType<typeof setInterval> | undefined;
  const stopSmartDownload = () => {
    clearInterval(smartTimer);
    smartTimer = undefined;
  };
  host.handle("settings.setSmartCleanup", ({ on }) => {
    save({ smartCleanup: on });
    return null;
  });
  host.handle("settings.pickSmartCleanup", ({ tier }) => {
    stopSmartDownload();
    if (tier === view.smartCleanupInstalled) {
      save({ smartCleanupTier: tier, smartCleanupDownloading: null, smartCleanupDownload: null });
      return null;
    }
    let fraction = 0;
    save({ smartCleanupTier: tier, smartCleanupDownloading: tier, smartCleanupDownload: smartDownloadStatus(tier, 0) });
    smartTimer = setInterval(() => {
      fraction += 0.1;
      if (fraction >= 1) {
        stopSmartDownload();
        save({ smartCleanup: true, smartCleanupInstalled: tier, smartCleanupDownloading: null, smartCleanupDownload: null });
        return;
      }
      save({ smartCleanupDownload: smartDownloadStatus(tier, fraction) });
    }, scenario.stepMs ?? 300);
    return null;
  });
  host.handle("settings.cancelSmartCleanup", () => {
    stopSmartDownload();
    save({ smartCleanupDownloading: null, smartCleanupDownload: null });
    return null;
  });
  host.handle("settings.removeSmartCleanup", () => {
    stopSmartDownload();
    save({ smartCleanup: false, smartCleanupInstalled: null, smartCleanupDownloading: null, smartCleanupDownload: null });
    return null;
  });
  host.handle("settings.cancelOfflinePack", () => {
    stopDownload();
    save({ downloadingPack: null, download: null });
    return null;
  });
}
