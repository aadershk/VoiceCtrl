import { useEffect, useState } from "react";
import type { AppKind, RunningApp, Style, StyleApp, StyleGroup, StyleView } from "../../bridge/contracts.g";
import { Button, Icon, Menu, MenuAnchor, MenuItem, Segmented, TextField } from "../../design/components";
import { cx } from "../../design/cx";
import { useHub } from "../hubContext";
import { rowDomId, screenById } from "../screens";
import { KIND_LABELS, KIND_NOTES, MOVE_APPS_ROW, STYLE_NOTES, STYLE_OPTIONS, YOUR_NAME_NOTE, YOUR_NAME_ROW } from "../styleCopy";
import { SAVE_FAILED } from "./DictionaryScreen";
import { ScreenHeader } from "./ScreenHeader";

const KINDS: readonly AppKind[] = ["personalChat", "workChat", "email", "document", "other", "aiPrompt"];

/**
 * Style (ticket 56): how a Transcription reads in each kind of app. One card per kind with its Style,
 * a live example in that Style (Core writes it, exactly as a Dictation would read), and the apps on
 * that card, each of which can be moved to another kind. AI tools and code have no Style. "Your
 * name" closes the page.
 */
export function StyleScreen() {
  const { bridge, toast, isLanding } = useHub();
  const { label, subtitle } = screenById("style");
  const [view, setView] = useState<StyleView | null>(null);

  useEffect(() => {
    void bridge.call("style.get", {}).then(setView, () => undefined);
  }, [bridge]);

  const apply = async (call: () => Promise<StyleView>, done?: string) => {
    try {
      setView(await call());
      if (done) toast(done);
    } catch {
      toast(SAVE_FAILED, { error: true });
    }
  };

  const setStyle = (kind: AppKind, style: Style) => {
    // Shown at once; the host's answer follows.
    setView((v) => v && { ...v, groups: v.groups.map((g) => (g.kind === kind ? { ...g, style } : g)) });
    void apply(() => bridge.call("style.set", { kind, style }));
  };

  const move = (app: StyleApp | RunningApp, kind: AppKind) => {
    const id = "id" in app ? app.id : app.processName;
    const name = "id" in app ? app.name : app.appName;
    void apply(() => bridge.call("style.moveApp", { app: id, kind }), `Moved ${name} to ${KIND_LABELS[kind]}`);
  };

  return (
    <div className="hub-screen">
      <ScreenHeader title={label} subtitle={subtitle} />
      {view && (
        <>
          <div className="hub-style-cards">
            {view.groups.map((group, i) => (
              <StyleCard
                key={group.kind}
                group={group}
                landingRow={i === 0 ? MOVE_APPS_ROW : undefined}
                landing={i === 0 && isLanding(MOVE_APPS_ROW)}
                onStyle={(style) => setStyle(group.kind, style)}
                onMove={move}
              />
            ))}
          </div>
          <YourName name={view.yourName} landing={isLanding(YOUR_NAME_ROW)} onSave={(name) => void apply(() => bridge.call("style.setName", { name }))} />
        </>
      )}
    </div>
  );
}

function StyleCard({ group, landingRow, landing, onStyle, onMove }: {
  group: StyleGroup;
  landingRow: string | undefined;
  landing: boolean;
  onStyle: (style: Style) => void;
  onMove: (app: StyleApp | RunningApp, kind: AppKind) => void;
}) {
  const example = group.examples.find((e) => e.style === group.style) ?? group.examples[0];
  const titleId = `style-${group.kind}`;
  return (
    <section className={cx("hub-style-card", group.style === null && "hub-style-card-plain")} aria-labelledby={titleId}>
      <div className="hub-style-head">
        <h2 id={titleId} className="hub-style-title">{KIND_LABELS[group.kind]}</h2>
        <span className="vc-meta">{KIND_NOTES[group.kind]}</span>
      </div>
      {group.style !== null && (
        <div className="hub-style-choice">
          <Segmented label={`${KIND_LABELS[group.kind]} style`} options={STYLE_OPTIONS} value={group.style} onChange={onStyle} />
          <span className="vc-meta">{STYLE_NOTES[group.style]}</span>
        </div>
      )}
      <figure className="hub-style-example">
        <figcaption>Example</figcaption>
        <p>{example?.text}</p>
      </figure>
      <div id={landingRow && rowDomId(landingRow)} className={cx("hub-style-apps", landing && "hub-landing-flash")}>
        {group.apps.map((app) => (
          <AppChip key={app.id} app={app} kind={group.kind} onMove={onMove} />
        ))}
        <AddApp kind={group.kind} onMove={onMove} />
      </div>
    </section>
  );
}

/** An app on a card. Clicking it offers the other kinds to move it to. */
function AppChip({ app, kind, onMove }: { app: StyleApp; kind: AppKind; onMove: (app: StyleApp, kind: AppKind) => void }) {
  const [open, setOpen] = useState(false);
  return (
    <span onKeyDown={(event) => event.key === "Escape" && open && event.stopPropagation()}>
      <MenuAnchor>
        <button
          type="button"
          className={cx("hub-style-app", app.moved && "hub-style-app-moved")}
          aria-haspopup="menu"
          aria-expanded={open}
          aria-label={`${app.name}: move to another kind of app`}
          onClick={() => setOpen((o) => !o)}
        >
          {app.name}
        </button>
        <Menu open={open} onClose={() => setOpen(false)} label={`Move ${app.name} to`} placement="below-start">
          {KINDS.map((target) => (
            <MenuItem key={target} selected={target === kind} onSelect={() => target !== kind && onMove(app, target)}>
              {KIND_LABELS[target]}
            </MenuItem>
          ))}
        </Menu>
      </MenuAnchor>
    </span>
  );
}

/** "Add app": one of the apps open now, moved onto this card. */
function AddApp({ kind, onMove }: { kind: AppKind; onMove: (app: RunningApp, kind: AppKind) => void }) {
  const { bridge } = useHub();
  const [open, setOpen] = useState(false);
  const [apps, setApps] = useState<RunningApp[] | null>(null);

  const toggle = () => {
    if (!open) {
      setApps(null);
      void bridge.call("profiles.runningApps", {}).then((r) => setApps(r.apps), () => setApps([]));
    }
    setOpen((o) => !o);
  };

  const elsewhere = apps?.filter((app) => app.kind !== kind) ?? [];
  return (
    <span onKeyDown={(event) => event.key === "Escape" && open && event.stopPropagation()}>
      <MenuAnchor>
        <Button variant="ghost" size="sm" icon="plus" aria-haspopup="menu" aria-expanded={open} onClick={toggle}>
          Add app
        </Button>
        <Menu open={open && apps !== null} onClose={() => setOpen(false)} label={`Add an open app to ${KIND_LABELS[kind]}`} placement="below-start">
          {elsewhere.length === 0 && (
            <MenuItem disabled onSelect={() => undefined}>
              No other apps are open. Open the app, then try again.
            </MenuItem>
          )}
          {elsewhere.map((app) => (
            <MenuItem key={app.processName} onSelect={() => onMove(app, kind)}>
              {app.appName}
              <span className="vc-meta hub-menu-meta">{KIND_LABELS[app.kind]}</span>
            </MenuItem>
          ))}
        </Menu>
      </MenuAnchor>
    </span>
  );
}

/** "Your name": saved when the field is left, or on Enter. */
function YourName({ name, landing, onSave }: { name: string; landing: boolean; onSave: (name: string) => void }) {
  const [draft, setDraft] = useState(name);
  useEffect(() => setDraft(name), [name]);
  const save = () => {
    if (draft.trim() !== name) onSave(draft);
  };
  return (
    <section className="hub-style-name" aria-label="Your name">
      <div id={rowDomId(YOUR_NAME_ROW)} className={cx("hub-style-name-field", landing && "hub-landing-flash")}>
        <TextField
          label="Your name"
          value={draft}
          placeholder="e.g. Alex"
          maxLength={60}
          onChange={(event) => setDraft(event.target.value)}
          onBlur={save}
          onKeyDown={(event) => event.key === "Enter" && save()}
        />
        <span className="vc-meta">
          <Icon name="mail" size={14} /> {YOUR_NAME_NOTE}
        </span>
      </div>
    </section>
  );
}
