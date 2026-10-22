import { useEffect, useRef, useState } from "react";
import type { DictionaryEntry } from "../../bridge/contracts.g";
import { Button, IconButton, InlineEditor, ListRow, TextField } from "../../design/components";
import { useFlipList } from "../../design/motion";
import { useHub } from "../hubContext";
import { WORD, count, wordError, wordMeta } from "../personalizationCopy";
import type { WordField } from "../personalizationCopy";
import { screenById } from "../screens";
import { ScreenHeader } from "./ScreenHeader";
import { EmptyList, ImportExportMenu, ListHeading, NoMatch, matches, useImportExport } from "./PersonalList";
import { SuggestionsCard } from "./SuggestionsCard";

/** Which word the open editor is for: null for Add. */
interface Editing {
  original: string | null;
  word: string;
  alsoHeardAs: string;
  error: { field: WordField; message: string } | null;
}

export const SAVE_FAILED = "That couldn't be saved. Try again.";

/**
 * Dictionary (research 10): Add word and "Import or export" in the header, the list with its
 * count and Filter, the inline editor (in place of the row, or on top for Add), Remove with
 * Undo, and the empty state. Above them, Dictionary Suggestions (slice 24): the Suggestions card,
 * or the Turn on card while the setting is off.
 */
export function DictionaryScreen() {
  const { bridge, toast } = useHub();
  const { label, subtitle } = screenById("dictionary");
  const [entries, setEntries] = useState<DictionaryEntry[] | null>(null);
  const [editing, setEditing] = useState<Editing | null>(null);
  const [filter, setFilter] = useState("");
  const list = useRef<HTMLDivElement>(null);
  useFlipList(list, [entries]);

  // A list that can't be read leaves the screen at its header, rather than claiming it is empty.
  const reload = () => void bridge.call("dictionary.list", {}).then((r) => setEntries(r.entries), () => undefined);
  useEffect(reload, [bridge]);

  const io = {
    noun: WORD,
    listName: "the Dictionary",
    onImport: () => bridge.call("dictionary.import", {}),
    onExport: () => bridge.call("dictionary.export", {}),
    onImported: reload,
  };
  const { importFile } = useImportExport(io);

  const openAdd = () => setEditing({ original: null, word: "", alsoHeardAs: "", error: null });

  const save = async () => {
    if (!editing) return;
    try {
      const result = await bridge.call("dictionary.save", {
        original: editing.original,
        word: editing.word,
        alsoHeardAs: editing.alsoHeardAs,
      });
      setEntries(result.entries);
      const error = wordError(result.error, editing.word);
      if (error) {
        setEditing({ ...editing, error });
        return;
      }
      setEditing(null);
      toast(`Saved ${editing.word.trim()}`);
    } catch {
      toast(SAVE_FAILED, { error: true });
    }
  };

  const remove = async (entry: DictionaryEntry) => {
    try {
      const { index, entries: after } = await bridge.call("dictionary.remove", { word: entry.word });
      setEntries(after);
      if (editing?.original === entry.word) setEditing(null);
      toast(`Removed ${entry.word}`, {
        action: {
          label: "Undo",
          onClick: () => void bridge.call("dictionary.restore", { entry, index }).then((r) => setEntries(r.entries)),
        },
      });
    } catch {
      toast(SAVE_FAILED, { error: true });
    }
  };

  const editor = editing && (
    <WordEditor
      editing={editing}
      onChange={(change) => setEditing({ ...editing, ...change })}
      onSave={() => void save()}
      onCancel={() => setEditing(null)}
    />
  );

  const shown = entries?.filter((e) => matches(filter, e.word, ...e.alsoHeardAs)) ?? [];

  return (
    <div className="hub-screen">
      <ScreenHeader
        title={label}
        subtitle={subtitle}
        actions={
          <>
            <ImportExportMenu {...io} canExport={(entries?.length ?? 0) > 0} />
            <Button variant="primary" icon="plus" onClick={openAdd}>
              Add word
            </Button>
          </>
        }
      />
      <SuggestionsCard entries={entries} onEntries={setEntries} />
      {editing?.original === null && editor}
      {entries && entries.length > 0 && (
        <section className="hub-list" aria-label="Your words">
          <ListHeading label={count(entries.length, WORD)} filter={filter} filterLabel="Filter words" onFilter={setFilter} />
          <div ref={list} className="hub-list-rows">
            {shown.map((entry, i) => (
              <div key={entry.word} data-flip-key={entry.word}>
                {editing?.original === entry.word ? (
                  editor
                ) : (
                  <ListRow
                    primary={entry.word}
                    meta={wordMeta(entry.alsoHeardAs)}
                    actions={
                      <>
                        <IconButton
                          icon="edit"
                          label={`Edit ${entry.word}`}
                          onClick={() =>
                            setEditing({ original: entry.word, word: entry.word, alsoHeardAs: entry.alsoHeardAs.join(", "), error: null })
                          }
                        />
                        <IconButton icon="delete" label={`Remove ${entry.word}`} onClick={() => void remove(entry)} />
                      </>
                    }
                  />
                )}
                {i < shown.length - 1 && <div className="vc-sep" />}
              </div>
            ))}
          </div>
          {shown.length === 0 && <NoMatch filter={filter} what="words" onClear={() => setFilter("")} />}
        </section>
      )}
      {entries && entries.length === 0 && editing?.original !== null && (
        <EmptyList art="dictionary" title="No words yet" onImport={() => void importFile()} addLabel="Add word" onAdd={openAdd}>
          Add names, product names and jargon, and VoiceCtrl will spell them your way in every Dictation.
        </EmptyList>
      )}
    </div>
  );
}

function WordEditor({ editing, onChange, onSave, onCancel }: {
  editing: Editing;
  onChange: (change: Partial<Editing>) => void;
  onSave: () => void;
  onCancel: () => void;
}) {
  const error = (field: WordField) => (editing.error?.field === field ? editing.error.message : undefined);
  return (
    <InlineEditor saveLabel="Save word" onSave={onSave} onCancel={onCancel}>
      <div className="hub-editor-pair">
        <TextField
          label="Word"
          value={editing.word}
          placeholder="e.g. Bluepeak"
          autoFocus
          maxLength={200}
          error={error("word")}
          onChange={(event) => onChange({ word: event.target.value, error: null })}
        />
        <TextField
          label="Also heard as"
          value={editing.alsoHeardAs}
          placeholder="Optional, e.g. blue peak"
          maxLength={400}
          error={error("alsoHeardAs")}
          onChange={(event) => onChange({ alsoHeardAs: event.target.value, error: null })}
        />
      </div>
    </InlineEditor>
  );
}
