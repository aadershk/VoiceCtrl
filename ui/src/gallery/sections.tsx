import { useRef, useState } from "react";
import type { Theme } from "../bridge/contracts.g";
import {
  ArtTile,
  Button,
  Dialog,
  EmptyState,
  FailedNotice,
  IconButton,
  InlineEditor,
  Keys,
  LatestHero,
  ListRow,
  Logo,
  Menu,
  MenuAnchor,
  MenuItem,
  MenuSeparator,
  NavList,
  ProgressBar,
  RadioCards,
  SearchField,
  Segmented,
  SettingsRow,
  Spinner,
  StatusCard,
  Switch,
  TextArea,
  TextField,
  Toast,
  TriggerChip,
  WindowControls,
} from "../design/components";
import { cascade, exit, exitRow, useFlipList } from "../design/motion";
import { Section } from "./Section";

const noop = () => undefined;
const MODES = [
  { value: "auto", label: "Auto" },
  { value: "online", label: "Online" },
  { value: "offline", label: "Offline" },
] as const;
type Mode = (typeof MODES)[number]["value"];

export function ButtonsSection() {
  return (
    <Section title="Buttons (§8)">
      <div className="g-row">
        <Button variant="primary" icon="paste">Paste Again</Button>
        <Button icon="copy">Copy</Button>
        <Button variant="ghost">Retry offline</Button>
        <Button variant="danger">Clear all</Button>
        <Button variant="primary" disabled>Continue</Button>
      </div>
      <div className="g-row">
        <Button variant="primary" size="sm" icon="plus">Add word</Button>
        <Button size="sm">Check now</Button>
        <Button variant="ghost" size="sm" trailingIcon="external">GitHub</Button>
        <IconButton icon="copy" label="Copy" />
        <IconButton icon="paste" label="Paste Again" />
        <IconButton icon="delete" label="Delete" />
        <IconButton icon="more" label="More" />
        <IconButton icon="close" label="Dismiss" size="sm" />
      </div>
    </Section>
  );
}

export function FieldsSection() {
  return (
    <Section title="Fields (§8)">
      <SearchField label="Quick Find" />
      <div className="g-grid-2">
        <TextField label="Word" placeholder="e.g. Bluepeak" defaultValue="Bluepeak" />
        <TextField label="Gemini API key" defaultValue="AIza-not-a-key" error="That key didn't work. Check it in AI Studio and paste it again, or use it anyway." />
      </div>
      <TextArea label="Expands to" placeholder="What VoiceCtrl types when you say the trigger" />
    </Section>
  );
}

export function TogglesSection() {
  const [on, setOn] = useState(true);
  const [mode, setMode] = useState<Mode>("auto");
  const [setupMode, setSetupMode] = useState<"online" | "offline">("online");
  return (
    <Section title="Switch, segmented control, radio cards (§8)">
      <div className="g-row">
        <Switch checked={on} onChange={setOn} label="Start VoiceCtrl with Windows" />
        <Switch checked={!on} onChange={(v) => setOn(!v)} label="Sound Cues" />
        <Segmented options={MODES} value={mode} onChange={setMode} label="Mode" />
      </div>
      <RadioCards
        label="How should it turn your speech into text?"
        value={setupMode}
        onChange={setSetupMode}
        options={[
          { value: "online", label: "Online", badge: "Recommended", icon: "cloud", description: "Best accuracy, using Google Gemini. Needs a free API key and an internet connection." },
          { value: "offline", label: "Offline", icon: "chip", description: "Runs entirely on this PC, so nothing ever leaves it. A one-time 700 MB download." },
        ]}
      />
    </Section>
  );
}

export function ChipsSection() {
  return (
    <Section title="Chips, spinner, progress (§8)">
      <div className="g-row">
        <Keys keys={["Ctrl", "Shift", "V"]} />
        <TriggerChip>my address</TriggerChip>
        <Spinner label="Checking" />
        <span className="vc-meta">62%</span>
        <ProgressBar value={0.62} label="Downloading 4.1" width={140} />
      </div>
    </Section>
  );
}

const NAV = [
  { id: "dictations", label: "Dictations", icon: "dictations" },
  { id: "dictionary", label: "Dictionary", icon: "dictionary" },
  { id: "snippets", label: "Snippets", icon: "snippets" },
  { id: "profiles", label: "Profiles", icon: "profiles" },
  { id: "settings", label: "Settings", icon: "settings" },
] as const;

export function SidebarSection({ theme }: { theme: Theme }) {
  const [screen, setScreen] = useState<(typeof NAV)[number]["id"]>("dictations");
  return (
    <Section title="Nav and status card (§8)">
      <div className="g-row" style={{ alignItems: "flex-start" }}>
        <div className="g-sidebar">
          <div className="g-brand">
            <Logo />
            <span className="vc-brand">VoiceCtrl</span>
          </div>
          <NavList items={NAV} current={screen} onSelect={setScreen} label="Hub" />
          <StatusCard state={{ kind: "ready", hotkey: "Ctrl", talkKeys: ["Left Ctrl", "Left Win"] }} theme={theme} onToggleTheme={noop} />
        </div>
        <div className="g-sidebar">
          <StatusCard
            state={{ kind: "warning", icon: "micOff", title: "No microphone", fix: "Choose one in Settings", onFix: noop }}
            theme={theme}
            onToggleTheme={noop}
          />
          <StatusCard
            state={{ kind: "warning", icon: "key", title: "API key not working", fix: "Using Offline until it's fixed", onFix: noop }}
            theme={theme}
            onToggleTheme={noop}
          />
        </div>
      </div>
    </Section>
  );
}

export function ListsSection() {
  const [keep, setKeep] = useState(true);
  const [mode, setMode] = useState<Mode>("auto");
  const rowActions = (
    <>
      <IconButton icon="copy" label="Copy" />
      <IconButton icon="paste" label="Paste Again" />
      <IconButton icon="delete" label="Delete" />
    </>
  );
  return (
    <Section title="List rows, Settings rows, inline editor (§8)">
      <div className="g-stack">
        <h3 className="vc-group">Today</h3>
        <ListRow primary="Next step is to compare the load factor on the Amsterdam to Lisbon route before and after the schedule change." meta="9:12 · Notion" actions={rowActions} />
        <div className="vc-sep" />
        <ListRow lead={<TriggerChip>my address</TriggerChip>} primary="Science Park 904, 1098 XH Amsterdam" actions={rowActions} />
      </div>
      <div className="g-stack">
        <SettingsRow label="Mode" description="How VoiceCtrl turns speech into text.">
          <Segmented options={MODES} value={mode} onChange={setMode} label="Mode" />
        </SettingsRow>
        <SettingsRow label="Start VoiceCtrl with Windows" description="Starts quietly by the clock when you sign in." flash>
          <Switch checked={keep} onChange={setKeep} label="Start VoiceCtrl with Windows" />
        </SettingsRow>
        <SettingsRow label="Automatic updates" description="Downloading 4.1 in the background.">
          <span className="vc-meta">62%</span>
          <ProgressBar value={0.62} label="Downloading 4.1" width={140} />
        </SettingsRow>
      </div>
      <InlineEditor saveLabel="Save word" onSave={noop} onCancel={noop}>
        <div className="g-grid-2">
          <TextField label="Word" defaultValue="Bluepeak" />
          <TextField label="Also heard as" placeholder="Optional, e.g. blue peak" />
        </div>
      </InlineEditor>
    </Section>
  );
}

export function NoticesSection() {
  return (
    <Section title="Latest hero, Failed Dictation notice, empty state (§8)">
      <LatestHero
        context="into Outlook · 2 min ago"
        text="Hi Maya, thanks for sending the draft over. I've gone through sections two and three and left comments on the forecasting part."
        pasteKeys={["Ctrl", "Shift", "V"]}
        onPasteAgain={noop}
        onCopy={noop}
      />
      <FailedNotice
        title="One Dictation didn't go through: no internet"
        detail="Slack · 9:41 · 12 s of audio, kept until you retry, dismiss it or quit"
        onRetryOffline={noop}
        onRetry={noop}
        onDismiss={noop}
      />
      <FailedNotice title="One Dictation didn't go through: no internet" detail="Slack · 9:41 · 12 s of audio" busy onRetry={noop} onDismiss={noop} />
      <EmptyState
        art={<ArtTile art="dictionary" />}
        title="No words yet"
        actions={
          <>
            <Button icon="import">Import</Button>
            <Button variant="primary" icon="plus">Add word</Button>
          </>
        }
      >
        Add names, product names and jargon, and VoiceCtrl will spell them your way in every Dictation.
      </EmptyState>
    </Section>
  );
}

export function PopoversSection() {
  const [mode, setMode] = useState<Mode>("auto");
  return (
    <Section title="Menu, tray flyout, dialog, toasts, window controls (§8, §12)" testId="popovers">
      <div className="g-stage g-stage-menu">
        <Menu open placement="static" label="Dictations" onClose={noop}>
          <MenuItem icon="copy" onSelect={noop}>Copy all as text</MenuItem>
          <MenuItem icon="delete" danger onSelect={noop}>Clear all Dictations</MenuItem>
        </Menu>
        <div className="g-flyout">
          <Menu open placement="static" label="VoiceCtrl" onClose={noop}>
            <div className="g-flyout-head">
              <span className="g-row"><Logo size={22} /><span className="vc-brand" style={{ fontSize: 15 }}>VoiceCtrl</span></span>
              <span className="vc-meta">Ready · Hold Left Ctrl + Left Win to talk</span>
            </div>
            <MenuSeparator />
            <MenuItem icon="window" onSelect={noop}>Open VoiceCtrl</MenuItem>
            <MenuItem icon="copy" onSelect={noop}>Copy latest Transcription</MenuItem>
            <MenuItem icon="pause" onSelect={noop}>Pause</MenuItem>
            <MenuSeparator />
            <div className="g-flyout-mode">Mode<Segmented options={MODES} value={mode} onChange={setMode} label="Mode" /></div>
            <MenuSeparator />
            <MenuItem icon="settings" onSelect={noop}>Settings</MenuItem>
            <MenuItem icon="power" onSelect={noop}>Quit VoiceCtrl</MenuItem>
          </Menu>
        </div>
      </div>
      <div className="g-stage">
        <Dialog open title="Clear all Dictations?" confirmLabel="Clear all" onConfirm={noop} onCancel={noop}>
          This removes all 42 Dictations from this PC. It can't be undone. Your Dictionary, Snippets and Profiles stay.
        </Dialog>
      </div>
      <div className="g-stage" style={{ minHeight: 200 }}>
        <div style={{ position: "absolute", inset: 0, bottom: 124 }}>
          <Toast open message="Diagnostics copied. Paste them into a GitHub issue." action={{ label: "Open GitHub", onClick: noop }} onClose={noop} />
        </div>
        <div style={{ position: "absolute", inset: 0, bottom: 66 }}>
          <Toast open message="Word deleted" action={{ label: "Undo", onClick: noop }} onClose={noop} />
        </div>
        <div style={{ position: "absolute", inset: 0, bottom: 8 }}>
          <Toast open error message="Couldn't read that file. Check it's a CSV with a header row." onClose={noop} />
        </div>
      </div>
      <div className="g-stage g-window">
        <WindowControls onMinimize={noop} onToggleMaximize={noop} onClose={noop} />
      </div>
    </Section>
  );
}

let nextRow = 4;

/** Interactive only (hidden from the visual baselines): each Glide pattern on demand. */
export function MotionSection() {
  const [rows, setRows] = useState([3, 2, 1]);
  const [menuOpen, setMenuOpen] = useState(false);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [toastOpen, setToastOpen] = useState(false);
  const [noticeKey, setNoticeKey] = useState(0);
  const listRef = useRef<HTMLDivElement>(null);
  const blocksRef = useRef<HTMLDivElement>(null);
  useFlipList(listRef, [rows]);

  const removeRow = async (id: number, el: HTMLElement | null) => {
    if (el) await exitRow(el);
    setRows((current) => current.filter((row) => row !== id));
  };
  const replayScreen = async () => {
    const blocks = blocksRef.current;
    if (!blocks) return;
    await exit(blocks);
    cascade(Array.from(blocks.children));
  };

  return (
    <Section title="Motion (§10): try each pattern" testId="motion">
      <div className="g-row">
        <Button size="sm" icon="plus" onClick={() => setRows((r) => [nextRow++, ...r])}>New row</Button>
        <Button size="sm" onClick={() => void replayScreen()}>Screen change</Button>
        <MenuAnchor>
          <Button size="sm" trailingIcon="chevronDown" onClick={() => setMenuOpen((o) => !o)}>Menu</Button>
          <Menu open={menuOpen} onClose={() => setMenuOpen(false)} label="Import or export" placement="below-start">
            <MenuItem icon="import" onSelect={noop}>Import from a file</MenuItem>
            <MenuItem icon="export" onSelect={noop}>Export to a file</MenuItem>
          </Menu>
        </MenuAnchor>
        <Button size="sm" onClick={() => setDialogOpen(true)}>Dialog</Button>
        <Button size="sm" onClick={() => setToastOpen(true)}>Toast</Button>
        <Button size="sm" onClick={() => setNoticeKey((k) => k + 1)}>Failed notice</Button>
      </div>
      <div ref={blocksRef} className="g-stack">
        <div ref={listRef} className="g-stack">
          {rows.map((id) => (
            <div key={id} data-flip-key={String(id)}>
              <ListRow
                primary={`Dictation ${id}`}
                meta="9:41 · Slack"
                actions={<IconButton icon="delete" label="Delete" onClick={(e) => void removeRow(id, e.currentTarget.closest("[data-flip-key]"))} />}
              />
            </div>
          ))}
        </div>
        {noticeKey > 0 && (
          <FailedNotice key={noticeKey} title="One Dictation didn't go through: no internet" detail="Slack · 9:41" onRetry={noop} onDismiss={() => setNoticeKey(0)} />
        )}
      </div>
      <Dialog open={dialogOpen} title="Clear all Dictations?" confirmLabel="Clear all" onConfirm={() => setDialogOpen(false)} onCancel={() => setDialogOpen(false)}>
        This removes all 42 Dictations from this PC. It can't be undone.
      </Dialog>
      <Toast open={toastOpen} message="Word deleted" action={{ label: "Undo", onClick: () => setToastOpen(false) }} onClose={() => setToastOpen(false)} />
    </Section>
  );
}
