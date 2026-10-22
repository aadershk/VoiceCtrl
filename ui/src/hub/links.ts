/**
 * The public repo's pages the Hub links to. The host opens github.com links in the browser
 * (WebSurface's external hosts) and never inside the app.
 */
export const GITHUB_URL = "https://github.com/aadershk/VoiceCtrl";

export const GITHUB_ISSUES_URL = `${GITHUB_URL}/issues/new`;

/** The release notes for one version, e.g. `…/releases/tag/v4.0.0`. */
export function releaseUrl(version: string): string {
  return `${GITHUB_URL}/releases/tag/v${version}`;
}

export function openLink(url: string): void {
  window.open(url, "_blank", "noopener");
}
