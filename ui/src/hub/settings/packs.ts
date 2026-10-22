import type { ModelDownloadStatus, OfflinePack, OfflinePackInfo } from "../../bridge/contracts.g";
import { downloadLine, downloadPercent, megabytes } from "../../setup/format";

/** What the Offline models row calls each pack. `none` is the English model Setup installs. */
export const PACK_NAMES: Record<OfflinePack, string> = {
  none: "English only",
  european: "European languages",
  allLanguages: "All languages",
};

/**
 * A pack's size as Windows writes file sizes. Setup's "700 MB" is the same measure, so the two
 * rows agree; the host's byte counts are the measured downloads (ticket 22).
 */
export function packSize(info: OfflinePackInfo): string {
  const mb = megabytes(info.bytes);
  return `${mb} MB`;
}

/** "25 languages", or "about 99 languages" for Whisper, which names 99 but is not exact about them. */
export function packLanguages(info: OfflinePackInfo): string {
  return info.pack === "allLanguages" ? `about ${info.languages} languages` : `${info.languages} languages`;
}

/** A menu item's text: the name and the facts a choice rests on. */
export function packMenuLabel(info: OfflinePackInfo): string {
  return [PACK_NAMES[info.pack], packLanguages(info), packSize(info), ...(info.slower ? ["slower"] : [])].join(" · ");
}

/** The line under "Offline models" while a pack downloads: what, how far, and the time left. */
export function packDownloadLine(pack: OfflinePack, download: ModelDownloadStatus): string {
  return `${PACK_NAMES[pack]} · ${downloadLine(download)}`;
}

/** The bar's 0 to 1, from the same rule as the percentage beside it. */
export function packFraction(download: ModelDownloadStatus): number {
  return downloadPercent(download) / 100;
}
