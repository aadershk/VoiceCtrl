import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ReactNode } from "react";
import type { Bridge } from "../bridge/bridge";
import type { AppSnapshot } from "../bridge/contracts.g";
import { Dialog, Toast, WindowControls } from "../design/components";
import { HubContext } from "./hubContext";
import type { ConfirmOptions, HubApi, ToastOptions } from "./hubContext";
import { ScreenStage } from "./ScreenStage";
import type { NavTarget } from "./ScreenStage";
import { isScreenId } from "./screens";
import type { ScreenId } from "./screens";
import { DictationsScreen } from "./screens/DictationsScreen";
import { DictionaryScreen } from "./screens/DictionaryScreen";
import { ProfilesScreen } from "./screens/ProfilesScreen";
import { SettingsScreen } from "./screens/SettingsScreen";
import { SnippetsScreen } from "./screens/SnippetsScreen";
import { StyleScreen } from "./screens/StyleScreen";
import { Sidebar } from "./Sidebar";
import { useAppState } from "./useAppState";

/** How long a landed Settings row flashes (DESIGN §8). */
export const LANDING_FLASH_MS = 1600;

// Both keep their content after closing (`open: false`), so it stays readable while it animates out.
interface ToastState extends ToastOptions {
  message: string;
  open: boolean;
}

interface DialogState extends ConfirmOptions {
  open: boolean;
  resolve: (confirmed: boolean) => void;
}

function defaultScreen(screen: ScreenId) {
  if (screen === "settings") return <SettingsScreen />;
  if (screen === "dictations") return <DictationsScreen />;
  if (screen === "dictionary") return <DictionaryScreen />;
  if (screen === "snippets") return <SnippetsScreen />;
  if (screen === "style") return <StyleScreen />;
  return <ProfilesScreen />;
}

export interface HubProps {
  bridge: Bridge;
  snapshot: AppSnapshot;
  /** Which component draws each screen. Tests pass their own; the app uses the real screens. */
  renderScreen?: (screen: ScreenId) => ReactNode;
  /** Where to open instead of Dictations, when the host asked for a place (see `startTargetFromQuery`). */
  start?: { screen: ScreenId; row?: string } | null;
}

/**
 * The Hub's frame (research 10, DESIGN §5): the window controls and drag strip, the sidebar,
 * the content card with its screen changes, and the toast and dialog every screen shares.
 * It opens on Dictations unless the host asked for another place (`start`, or `hub.show` later).
 */
export function Hub({ bridge, snapshot, renderScreen = defaultScreen, start = null }: HubProps) {
  const app = useAppState(bridge, snapshot);
  const [target, setTarget] = useState<NavTarget>(() => ({ screen: start?.screen ?? "dictations", row: start?.row, seq: 0 }));
  const [landing, setLanding] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState | null>(null);
  const [dialog, setDialog] = useState<DialogState | null>(null);
  // Dismissed here at once; the host forgets it for good (app.dismissUpdateCard).
  const [updatedTo, setUpdatedTo] = useState(snapshot.updatedTo);

  const go = useCallback((screen: ScreenId, row?: string) => {
    setTarget((t) => ({ screen, row, seq: t.seq + 1 }));
  }, []);

  // The host moves an open Hub (the Overlay's "Add an API key"). The page is a system boundary,
  // so an unknown screen is ignored.
  useEffect(
    () =>
      bridge.on("hub.show", ({ screen, row }) => {
        if (isScreenId(screen)) go(screen, row ?? undefined);
      }),
    [bridge, go],
  );

  // Landing again on the row that is still flashing replays the flash: the class comes off for
  // one frame, so the browser sees it as new.
  const landingNow = useRef<string | null>(null);
  landingNow.current = landing;
  const onLanded = useCallback((row: string) => {
    if (landingNow.current !== row) {
      setLanding(row);
      return;
    }
    setLanding(null);
    requestAnimationFrame(() => setLanding(row));
  }, []);
  useEffect(() => {
    if (landing === null) return;
    const timer = setTimeout(() => setLanding(null), LANDING_FLASH_MS);
    return () => clearTimeout(timer);
  }, [landing]);

  const api = useMemo<HubApi>(
    () => ({
      bridge,
      app,
      go,
      toast: (message, options = {}) => setToast({ ...options, message, open: true }),
      confirm: (options) =>
        new Promise<boolean>((resolve) => {
          setDialog({ ...options, open: true, resolve });
        }),
      isLanding: (row) => landing === row,
    }),
    [bridge, app, go, landing],
  );

  const closeDialog = (confirmed: boolean) => {
    if (!dialog?.open) return;
    dialog.resolve(confirmed);
    setDialog({ ...dialog, open: false });
  };
  const closeToast = () => setToast((t) => t && { ...t, open: false });

  return (
    <HubContext.Provider value={api}>
      <div className="hub">
        {/* The title strip moves the window (spec §2); the window controls opt out with no-drag. */}
        <div className="hub-drag" aria-hidden="true" />
        <WindowControls
          maximized={app.maximized}
          onMinimize={() => void bridge.call("window.minimize", {})}
          onToggleMaximize={() => void bridge.call("window.toggleMaximize", {})}
          onClose={() => void bridge.call("window.close", {})}
        />
        <Sidebar
          current={target.screen}
          status={app.status}
          theme={app.theme}
          go={go}
          onToggleTheme={() => void bridge.call("app.setTheme", { theme: app.theme === "light" ? "dark" : "light" })}
          onResume={() => void bridge.call("app.setPaused", { paused: false })}
          updatedTo={updatedTo}
          onDismissUpdate={() => {
            setUpdatedTo(null);
            void bridge.call("app.dismissUpdateCard", {});
          }}
        />
        <main className="hub-card" aria-label="Content">
          <ScreenStage target={target} render={renderScreen} onLanded={onLanded} />
          <Toast
            open={toast?.open ?? false}
            message={toast?.message ?? ""}
            error={toast?.error ?? false}
            action={
              toast?.action && {
                label: toast.action.label,
                // Close first: the action may show a toast of its own ("Restored"), which must stay.
                onClick: () => {
                  closeToast();
                  toast.action?.onClick();
                },
              }
            }
            onClose={closeToast}
          />
        </main>
        <Dialog
          open={dialog?.open ?? false}
          title={dialog?.title ?? ""}
          confirmLabel={dialog?.confirmLabel ?? ""}
          onConfirm={() => closeDialog(true)}
          onCancel={() => closeDialog(false)}
        >
          {dialog?.body}
        </Dialog>
      </div>
    </HubContext.Provider>
  );
}
