import type { ModelDownloadStatus } from "../bridge/contracts.g";

const MiB = 1024 * 1024;

/** Whole megabytes as the board writes them ("312 of 700 MB"). */
export function megabytes(bytes: number): number {
  return Math.round(bytes / MiB);
}

/**
 * The percentage beside the bar. It stays at 99 until the host says the download is done: the
 * total is an estimate (LocalModelDownloader.ApproxTotalBytes), so the bytes alone never finish it.
 */
export function downloadPercent(model: ModelDownloadStatus): number {
  if (model.state === "done") return 100;
  if (model.totalBytes <= 0) return 0;
  return Math.min(99, Math.floor((model.bytes * 100) / model.totalBytes));
}

/** "about 40 s left", "about 3 min left". Rounded, because the rate is an average. */
export function timeLeft(remainingBytes: number, bytesPerSecond: number): string | null {
  if (bytesPerSecond <= 0 || remainingBytes <= 0) return null;
  const seconds = remainingBytes / bytesPerSecond;
  if (seconds < 55) return `about ${Math.max(5, Math.round(seconds / 5) * 5)} s left`;
  return `about ${Math.max(1, Math.round(seconds / 60))} min left`;
}

/** The line under "English speech model" (research 13, step 2b). */
export function downloadLine(model: ModelDownloadStatus): string {
  const total = megabytes(model.totalBytes);
  if (model.state === "done") return `Installed · ${total} MB · ready to use`;
  const soFar = `${Math.min(megabytes(model.bytes), total)} of ${total} MB`;
  if (model.state === "failed") return `${soFar} · stopped`;
  if (model.state === "downloading") {
    const left = timeLeft(model.totalBytes - model.bytes, model.bytesPerSecond);
    if (left) return `${soFar} · ${left}`;
  }
  return soFar;
}
