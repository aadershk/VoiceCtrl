import { Fragment } from "react";
import type { ReactNode } from "react";
import type { ThemePreference } from "../../bridge/contracts.g";
import { Segmented, SettingsRow } from "../../design/components";
import { useHub } from "../hubContext";
import { rowDomId, screenById, settingsGroups, settingsRows } from "../screens";
import { AboutCard } from "../settings/AboutCard";
import { useDictationRows } from "../settings/dictationRows";
import { useGeneralRows } from "../settings/generalRows";
import { useSuggestionRows } from "../settings/suggestionRows";
import { useTranscriptionRows } from "../settings/transcriptionRows";
import { ScreenHeader } from "./ScreenHeader";

const themeOptions = [
  { value: "light", label: "Light" },
  { value: "dark", label: "Dark" },
  { value: "system", label: "System" },
] as const satisfies readonly { value: ThemePreference; label: string }[];

/**
 * Settings: one scrolling page of group headings (research 10). Every row from `settingsRows` is
 * here, so Quick Find can land on it; each Settings slice fills in its group's controls (the
 * Dictation group is `useDictationRows`). A group may also give a row a note, which stands in for
 * its description while something needs saying. The About card closes the page (DESIGN §12).
 */
export function SettingsScreen() {
  const { bridge, app, isLanding } = useHub();
  const { label, subtitle } = screenById("settings");
  const dictation = useDictationRows();
  const transcription = useTranscriptionRows();
  const suggestions = useSuggestionRows();
  const general = useGeneralRows();

  const controls: Record<string, ReactNode> = {
    ...dictation.controls,
    ...transcription.controls,
    ...suggestions.controls,
    ...general.controls,
    theme: (
      <Segmented
        label="Theme"
        options={themeOptions}
        value={app.themePreference}
        onChange={(theme) => void bridge.call("app.setTheme", { theme })}
      />
    ),
  };
  const notes: Record<string, ReactNode> = {
    ...dictation.notes,
    ...transcription.notes,
    ...suggestions.notes,
    ...general.notes,
  };
  const descriptions: Record<string, ReactNode> = {
    ...dictation.descriptions,
    ...transcription.descriptions,
    ...general.descriptions,
  };

  const labels: Record<string, string> = { ...transcription.labels };
  const hidden = new Set(transcription.hidden ?? []);

  return (
    <div className="hub-screen">
      <ScreenHeader title={label} subtitle={subtitle} />
      {settingsGroups.map((group) => {
        const rows = settingsRows.filter((row) => row.group === group && !hidden.has(row.id));
        return (
          <section key={group} className="hub-group" aria-label={group}>
            <h2 className="vc-group">{group}</h2>
            {rows.map((row, i) => (
              <Fragment key={row.id}>
                {i > 0 && <div className="vc-sep" />}
                <SettingsRow
                  id={rowDomId(row.id)}
                  label={labels[row.id] ?? row.label}
                  description={
                    notes[row.id] ? (
                      <span className="hub-setting-note" role="status">
                        {notes[row.id]}
                      </span>
                    ) : (
                      descriptions[row.id] ?? row.description
                    )
                  }
                  flash={isLanding(row.id)}
                >
                  {controls[row.id] ?? null}
                </SettingsRow>
              </Fragment>
            ))}
          </section>
        );
      })}
      <AboutCard version={app.version} />
    </div>
  );
}
