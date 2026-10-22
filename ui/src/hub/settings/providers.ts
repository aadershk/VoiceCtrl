import type { OnlineProvider } from "../../bridge/contracts.g";

/**
 * The Online providers as Setup and Settings name them (ticket 60). The host's preset table holds
 * the addresses and models; the page only needs names, where a key comes from and what one looks like.
 * The order is the pickers' order: Gemini, the default, first.
 */
export const PROVIDERS: readonly { value: OnlineProvider; label: string }[] = [
  { value: "gemini", label: "Gemini" },
  { value: "nvidia", label: "NVIDIA" },
  { value: "openAi", label: "OpenAI" },
  { value: "other", label: "Other" },
];

export const PROVIDER_NAMES: Record<OnlineProvider, string> = {
  gemini: "Gemini",
  nvidia: "NVIDIA",
  openAi: "OpenAI",
  other: "Other",
};

/** Where to get a key; Other has no one place. The host lets the page open only these hosts. */
export const KEY_PAGES: Record<OnlineProvider, { url: string; site: string; steps: string } | null> = {
  gemini: { url: "https://aistudio.google.com/apikey", site: "AI Studio", steps: "Sign in, then click Create API key." },
  nvidia: { url: "https://build.nvidia.com/settings/api-keys", site: "NVIDIA Build", steps: "Sign in, then click Generate API Key." },
  openAi: { url: "https://platform.openai.com/api-keys", site: "OpenAI", steps: "Sign in, then click Create new secret key." },
  other: null,
};

export const KEY_HINTS: Record<OnlineProvider, string> = {
  gemini: "Starts with AIza",
  nvidia: "Starts with nvapi-",
  openAi: "Starts with sk-",
  other: "Your provider's API key",
};

/** "Gemini API key", "Other API key". */
export function keyName(provider: OnlineProvider): string {
  return `${PROVIDER_NAMES[provider]} API key`;
}
