import { Logo, NavList, StatusCard } from "../design/components";
import type { NavEntry } from "../design/components";
import type { AppStatus, Theme } from "../bridge/contracts.g";
import { QuickFind } from "./QuickFind";
import { UpdateCard } from "./UpdateCard";
import { openLink, releaseUrl } from "./links";
import { screens } from "./screens";
import type { ScreenId } from "./screens";
import { statusCardState } from "./statusCard";

const navItems: readonly NavEntry<ScreenId>[] = screens.map(({ id, label, icon }) => ({ id, label, icon }));

export interface SidebarProps {
  current: ScreenId;
  status: AppStatus;
  theme: Theme;
  go: (screen: ScreenId, row?: string) => void;
  onToggleTheme: () => void;
  onResume: () => void;
  /** The "Updated to" card's version, or null for none. */
  updatedTo: string | null;
  onDismissUpdate: () => void;
}

/** The sidebar (research 10): logo, Quick Find, the five screens, then the update card (after an
 * update) and the status card. It never moves. */
export function Sidebar({ current, status, theme, go, onToggleTheme, onResume, updatedTo, onDismissUpdate }: SidebarProps) {
  const card = statusCardState(status, {
    openSettingsRow: (row) => go("settings", row),
    resume: onResume,
  });
  return (
    <aside className="hub-sidebar">
      <div className="hub-brand">
        <Logo size={28} />
        <span className="vc-brand">VoiceCtrl</span>
      </div>
      <QuickFind onPick={(entry) => go(entry.screen, entry.row)} />
      <NavList items={navItems} current={current} onSelect={(id) => go(id)} label="Hub" />
      <div className="hub-sidebar-spacer" />
      {updatedTo !== null && (
        <UpdateCard
          version={updatedTo}
          onDismiss={onDismissUpdate}
          onWhatsNew={() => {
            openLink(releaseUrl(updatedTo));
            onDismissUpdate();
          }}
        />
      )}
      <StatusCard state={card} theme={theme} onToggleTheme={onToggleTheme} />
    </aside>
  );
}
