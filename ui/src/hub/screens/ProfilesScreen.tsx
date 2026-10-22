import { useEffect, useRef, useState } from "react";
import type { ProfileEntry, RunningApp } from "../../bridge/contracts.g";
import {
  ArtTile,
  Button,
  EmptyState,
  Icon,
  IconButton,
  InlineEditor,
  ListRow,
  Menu,
  MenuAnchor,
  MenuItem,
  Segmented,
  Switch,
  TextField,
} from "../../design/components";
import { useFlipList } from "../../design/motion";
import { useHub } from "../hubContext";
import {
  CLEANUP_OPTIONS,
  EMAIL_LAYOUT_NOTE,
  FORMATTING_OPTIONS,
  INSTRUCTIONS_PLACEHOLDER,
  cleanupNote,
  exeName,
  profileError,
  profileSummary,
  styleNote,
  styleOptions,
} from "../profileCopy";
import type { ProfileField } from "../profileCopy";
import { screenById } from "../screens";
import { SAVE_FAILED } from "./DictionaryScreen";
import { ScreenHeader } from "./ScreenHeader";

/** Which Profile the open editor is for: `original` null for Add. */
interface Editing {
  original: string | null;
  draft: ProfileEntry;
  /** Add only: the Formatting choice still follows the app picked, until the user picks one. */
  formattingTouched: boolean;
  error: { field: ProfileField; message: string } | null;
}

const BLANK: ProfileEntry = {
  processName: "",
  appName: "",
  tone: "",
  formatting: "none",
  cleanup: "default",
  emailLayout: false,
  instructions: "",
  style: "",
  kind: "other",
};

const sameApp = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();

/**
 * Profiles (research 10): per-app rules for how a Transcription reads. Rows show the app and a
 * summary line; Edit opens the editor in place (app, Style, How I write here, Formatting, Cleanup,
 * Email Layout),
 * Add opens it on top with the list of open apps ready to pick from.
 */
export function ProfilesScreen() {
  const { bridge, toast } = useHub();
  const { label, subtitle } = screenById("profiles");
  const [profiles, setProfiles] = useState<ProfileEntry[] | null>(null);
  const [editing, setEditing] = useState<Editing | null>(null);
  const [apps, setApps] = useState<RunningApp[] | null>(null);
  const [defaultCleanup, setDefaultCleanup] = useState<string | null>(null);
  const list = useRef<HTMLDivElement>(null);
  useFlipList(list, [profiles]);

  useEffect(() => {
    void bridge.call("profiles.list", {}).then((r) => setProfiles(r.profiles), () => undefined);
    void bridge.call("settings.getTranscription", {}).then((r) => setDefaultCleanup(r.defaultCleanup), () => undefined);
    return bridge.on("settings.transcription", (e) => setDefaultCleanup(e.transcription.defaultCleanup));
  }, [bridge]);

  // Asked again each time the editor opens, since apps come and go.
  const loadApps = () => {
    setApps(null);
    void bridge.call("profiles.runningApps", {}).then((r) => setApps(r.apps), () => setApps([]));
  };

  const open = (original: string | null, draft: ProfileEntry) => {
    loadApps();
    setEditing({ original, draft, formattingTouched: original !== null, error: null });
  };

  const save = async () => {
    if (!editing) return;
    const { draft } = editing;
    try {
      const result = await bridge.call("profiles.save", { original: editing.original, profile: draft });
      setProfiles(result.profiles);
      const error = profileError(result.error, draft.appName);
      if (error?.field) {
        setEditing({ ...editing, error: { field: error.field, message: error.message } });
        return;
      }
      if (error) {
        toast(error.message, { error: true });
        return;
      }
      setEditing(null);
      toast(`Saved the ${draft.appName} profile`);
    } catch {
      toast(SAVE_FAILED, { error: true });
    }
  };

  const remove = async (profile: ProfileEntry) => {
    try {
      const result = await bridge.call("profiles.remove", { processName: profile.processName });
      setProfiles(result.profiles);
      setEditing(null);
      toast(`Deleted the ${profile.appName} profile`, {
        action: {
          label: "Undo",
          onClick: () => void bridge.call("profiles.restore", { profile }).then((r) => setProfiles(r.profiles)),
        },
      });
    } catch {
      toast(SAVE_FAILED, { error: true });
    }
  };

  const editor = editing && (
    <ProfileEditor
      editing={editing}
      apps={apps}
      defaultCleanup={defaultCleanup}
      onChange={setEditing}
      onSave={() => void save()}
      onCancel={() => setEditing(null)}
      onDelete={editing.original === null ? null : () => void remove(profiles?.find((p) => sameApp(p.processName, editing.original ?? "")) ?? editing.draft)}
    />
  );

  return (
    <div className="hub-screen">
      <ScreenHeader
        title={label}
        subtitle={subtitle}
        actions={
          <Button variant="primary" icon="plus" onClick={() => open(null, BLANK)}>
            Add profile
          </Button>
        }
      />
      {editing?.original === null && editor}
      {profiles && profiles.length > 0 && (
        <section className="hub-list" aria-label="Your profiles">
          <div className="hub-list-heading">
            <h2 className="vc-group">{profiles.length === 1 ? "1 profile" : `${profiles.length} profiles`}</h2>
          </div>
          <div ref={list} className="hub-list-rows">
            {profiles.map((profile, i) => (
              <div key={profile.processName} data-flip-key={profile.processName}>
                {editing?.original !== null && editing?.original !== undefined && sameApp(editing.original, profile.processName) ? (
                  editor
                ) : (
                  <ListRow
                    lead={<AppTile name={profile.appName} />}
                    primary={profile.appName}
                    meta={profileSummary(profile)}
                    actions={
                      <IconButton icon="edit" label={`Edit the ${profile.appName} profile`} onClick={() => open(profile.processName, profile)} />
                    }
                  />
                )}
                {i < profiles.length - 1 && <div className="vc-sep" />}
              </div>
            ))}
          </div>
        </section>
      )}
      {profiles && profiles.length === 0 && editing?.original !== null && (
        <EmptyState
          art={<ArtTile art="profiles" />}
          title="No Profiles yet"
          actions={
            <Button variant="primary" icon="plus" onClick={() => open(null, BLANK)}>
              Add profile
            </Button>
          }
        >
          Every app uses your defaults from Settings. Add a Profile to make Outlook more formal, Slack more casual, or VS Code word
          for word.
        </EmptyState>
      )}
    </div>
  );
}

/** The app's initial on a `--tint` tile, the row's lead. */
function AppTile({ name }: { name: string }) {
  return (
    <span className="hub-app-tile" aria-hidden="true">
      {name.charAt(0).toUpperCase()}
    </span>
  );
}

function ProfileEditor({ editing, apps, defaultCleanup, onChange, onSave, onCancel, onDelete }: {
  editing: Editing;
  apps: RunningApp[] | null;
  defaultCleanup: string | null;
  onChange: (editing: Editing) => void;
  onSave: () => void;
  onCancel: () => void;
  onDelete: (() => void) | null;
}) {
  const { draft } = editing;
  const change = (fields: Partial<ProfileEntry>, touched = editing.formattingTouched) =>
    onChange({ ...editing, draft: { ...draft, ...fields }, formattingTouched: touched, error: null });
  const error = (field: ProfileField) => (editing.error?.field === field ? editing.error.message : undefined);

  const pick = (app: RunningApp) =>
    change({
      processName: app.processName,
      appName: app.appName,
      kind: app.kind,
      // A new Profile starts from what the app gets today, so saving it alone changes nothing.
      ...(editing.formattingTouched ? {} : { formatting: app.formatting, emailLayout: app.emailLayout }),
    });

  return (
    <InlineEditor
      saveLabel="Save profile"
      onSave={onSave}
      onCancel={onCancel}
      leading={
        onDelete && (
          <Button variant="ghost" icon="delete" className="hub-danger-ghost" onClick={onDelete}>
            Delete profile
          </Button>
        )
      }
    >
      <AppPicker
        draft={draft}
        apps={apps}
        original={editing.original}
        error={error("app")}
        openAtStart={editing.original === null}
        onPick={pick}
      />
      <div className="vc-field">
        <span aria-hidden="true">Style</span>
        <Segmented label="Style" options={styleOptions(draft.kind)} value={draft.style} onChange={(style) => change({ style })} />
        <span className="vc-meta">{styleNote(draft.kind)}</span>
      </div>
      <TextField
        label="How I write here"
        value={draft.instructions}
        placeholder={INSTRUCTIONS_PLACEHOLDER}
        maxLength={400}
        error={error("instructions")}
        onChange={(event) => change({ instructions: event.target.value })}
      />
      <div className="vc-field">
        <span aria-hidden="true">Formatting</span>
        <Segmented label="Formatting" options={FORMATTING_OPTIONS} value={draft.formatting} onChange={(formatting) => change({ formatting }, true)} />
      </div>
      <div className="vc-field">
        <span aria-hidden="true">Cleanup</span>
        <Segmented label="Cleanup" options={CLEANUP_OPTIONS} value={draft.cleanup} onChange={(cleanup) => change({ cleanup })} />
        <span className="vc-meta">{cleanupNote(defaultCleanup)}</span>
      </div>
      <div className="hub-switch-panel">
        <div className="hub-switch-panel-text">
          <b>Email Layout</b>
          <span className="vc-meta">{EMAIL_LAYOUT_NOTE}</span>
        </div>
        <Switch label="Email Layout" checked={draft.emailLayout} onChange={(emailLayout) => change({ emailLayout }, true)} />
      </div>
    </InlineEditor>
  );
}

/** The editor's top line: the app with its exe and "Change app", or "Choose an app" for a new Profile. */
function AppPicker({ draft, apps, original, error, openAtStart, onPick }: {
  draft: ProfileEntry;
  apps: RunningApp[] | null;
  original: string | null;
  error: string | undefined;
  openAtStart: boolean;
  onPick: (app: RunningApp) => void;
}) {
  const [open, setOpen] = useState(openAtStart);
  const chosen = draft.processName.length > 0;
  return (
    <div className="hub-app-picker">
      <div className="hub-app-picker-line">
        {chosen ? <AppTile name={draft.appName} /> : <span className="hub-app-tile hub-app-tile-empty" aria-hidden="true"><Icon name="plus" size={16} /></span>}
        <div className="vc-row-text">
          <span className="hub-app-picker-name">{chosen ? draft.appName : "Choose an app"}</span>
          <span className="vc-meta">{chosen ? exeName(draft.processName) : "Pick one of the apps open now."}</span>
        </div>
        {/* Esc closes the menu only, not the whole editor around it. */}
        <span onKeyDown={(event) => event.key === "Escape" && open && event.stopPropagation()}>
          <MenuAnchor>
            <Button
              variant={chosen ? "ghost" : "secondary"}
              size="sm"
              trailingIcon="chevronDown"
              aria-haspopup="menu"
              aria-expanded={open}
              onClick={() => setOpen((o) => !o)}
            >
              {chosen ? "Change app" : "Choose app"}
            </Button>
            <Menu open={open && apps !== null} onClose={() => setOpen(false)} label="Open apps" placement="below-end">
              {apps?.length === 0 && (
                <MenuItem disabled onSelect={() => undefined}>
                  No apps are open. Open the app, then try again.
                </MenuItem>
              )}
              {apps?.map((app) => {
                const taken = app.hasProfile && !(original !== null && sameApp(original, app.processName));
                return (
                  <MenuItem key={app.processName} disabled={taken} selected={sameApp(app.processName, draft.processName)} onSelect={() => onPick(app)}>
                    {app.appName}
                    <span className="vc-meta hub-menu-meta">{taken ? "Has a profile" : exeName(app.processName)}</span>
                  </MenuItem>
                );
              })}
            </Menu>
          </MenuAnchor>
        </span>
      </div>
      {error && (
        <p className="vc-field-error" role="alert">
          <Icon name="alert" size={16} />
          {error}
        </p>
      )}
    </div>
  );
}
