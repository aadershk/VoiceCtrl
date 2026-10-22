import { createContext, useContext } from "react";
import type { Bridge } from "../bridge/bridge";
import type { AppState } from "./useAppState";
import type { ScreenId } from "./screens";

export interface ToastOptions {
  /** Error toasts are `--panel` with a danger icon (DESIGN §8). */
  error?: boolean;
  /** Undo, or another single action; the toast then stays 5 s. */
  action?: { label: string; onClick: () => void };
}

export interface ConfirmOptions {
  title: string;
  body: string;
  confirmLabel: string;
}

/** What the Hub frame offers every screen. */
export interface HubApi {
  bridge: Bridge;
  app: AppState;
  /** Opens a screen; with a row, scrolls it to the centre and flashes it (Quick Find landing). */
  go(screen: ScreenId, row?: string): void;
  /** One toast at a time, at the bottom centre of the content card; a new one replaces it. */
  toast(message: string, options?: ToastOptions): void;
  /** The one confirm dialog (§11: undo instead of confirm everywhere else). True on confirm. */
  confirm(options: ConfirmOptions): Promise<boolean>;
  /** True for 1.6 s after Quick Find landed on this row; pass it to the row's `flash`. */
  isLanding(row: string): boolean;
}

export const HubContext = createContext<HubApi | null>(null);

export function useHub(): HubApi {
  const hub = useContext(HubContext);
  if (!hub) throw new Error("useHub() outside the Hub frame");
  return hub;
}
