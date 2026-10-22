import type { ModelDownloadStatus, SmartCleanupPackInfo, SmartCleanupTier } from "../../bridge/contracts.g";
import { downloadLine, downloadPercent } from "../../setup/format";

/** What the Smart cleanup model row calls each model. */
export const TIER_NAMES: Record<SmartCleanupTier, string> = {
  standard: "Standard",
  better: "Better",
};

/** What each model is for, in the words the row uses (ticket 37's measured results). */
export const TIER_HINTS: Record<SmartCleanupTier, string> = {
  standard: "quicker, about 2 seconds more per Dictation, but misses more",
  better: "closer to what Online writes, about 5 seconds more per Dictation",
};

/** A download size as "2.3 GB", to one decimal, the way Windows writes file sizes: the same measure
 * as the "2400 MB" the progress line shows beside it. */
export function gigabytes(bytes: number): string {
  return `${(bytes / 1024 ** 3).toFixed(1)} GB`;
}

/** A segment's text: the name and what it costs on disk. */
export function tierLabel(info: SmartCleanupPackInfo): string {
  return `${TIER_NAMES[info.tier]} · ${gigabytes(info.bytes)}`;
}

/** The line under the row while a model downloads: which, how far, and the time left. */
export function tierDownloadLine(tier: SmartCleanupTier, download: ModelDownloadStatus): string {
  return `${TIER_NAMES[tier]} model · ${downloadLine(download)}`;
}

/** The bar's 0 to 1, from the same rule as the percentage beside it. */
export function tierFraction(download: ModelDownloadStatus): number {
  return downloadPercent(download) / 100;
}
