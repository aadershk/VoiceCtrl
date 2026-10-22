import { useEffect, useRef, useState } from "react";
import type { Snippet } from "../../bridge/contracts.g";
import { Button, Icon, IconButton, InlineEditor, ListRow, TextArea, TextField, TriggerChip } from "../../design/components";
import { useFlipList } from "../../design/motion";
import { useHub } from "../hubContext";
import { SNIPPET, count, snippetError } from "../personalizationCopy";
import type { SnippetField } from "../personalizationCopy";
import { screenById } from "../screens";
import { SAVE_FAILED } from "./DictionaryScreen";
import { ScreenHeader } from "./ScreenHeader";
import { EmptyList, ImportExportMenu, ListHeading, NoMatch, matches, useImportExport } from "./PersonalList";

/** Which Snippet the open editor is for: null for Add. */
interface Editing {
  original: string | null;
  trigger: string;
  expansion: string;
  error: { field: SnippetField; message: string } | null;
}

/**
 * Snippets (research 10): the Dictionary screen's shape, with rows of a trigger chip → the
 * expansion on one line, and an editor of "When you say" and "VoiceCtrl types" (multi-line).
 */
export function SnippetsScreen() {
  const { bridge, toast } = useHub();
  const { label, subtitle } = screenById("snippets");
  const [snippets, setSnippets] = useState<Snippet[] | null>(null);
  const [editing, setEditing] = useState<Editing | null>(null);
  const [filter, setFilter] = useState("");
  const list = useRef<HTMLDivElement>(null);
  useFlipList(list, [snippets]);

  const reload = () => void bridge.call("snippets.list", {}).then((r) => setSnippets(r.snippets), () => undefined);
  useEffect(reload, [bridge]);

  const io = {
    noun: SNIPPET,
    listName: "Snippets",
    onImport: () => bridge.call("snippets.import", {}),
    onExport: () => bridge.call("snippets.export", {}),
    onImported: reload,
  };
  const { importFile } = useImportExport(io);

  const openAdd = () => setEditing({ original: null, trigger: "", expansion: "", error: null });

  const save = async () => {
    if (!editing) return;
    try {
      const result = await bridge.call("snippets.save", {
        original: editing.original,
        trigger: editing.trigger,
        expansion: editing.expansion,
      });
      setSnippets(result.snippets);
      const error = snippetError(result.error, editing.trigger);
      if (error) {
        setEditing({ ...editing, error });
        return;
      }
      setEditing(null);
      toast(`Saved “${editing.trigger.trim()}”`);
    } catch {
      toast(SAVE_FAILED, { error: true });
    }
  };

  const remove = async (snippet: Snippet) => {
    try {
      const { index, snippets: after } = await bridge.call("snippets.remove", { trigger: snippet.trigger });
      setSnippets(after);
      if (editing?.original === snippet.trigger) setEditing(null);
      toast(`Removed “${snippet.trigger}”`, {
        action: {
          label: "Undo",
          onClick: () => void bridge.call("snippets.restore", { snippet, index }).then((r) => setSnippets(r.snippets)),
        },
      });
    } catch {
      toast(SAVE_FAILED, { error: true });
    }
  };

  const editor = editing && (
    <SnippetEditor
      editing={editing}
      onChange={(change) => setEditing({ ...editing, ...change })}
      onSave={() => void save()}
      onCancel={() => setEditing(null)}
    />
  );

  const shown = snippets?.filter((s) => matches(filter, s.trigger, s.expansion)) ?? [];

  return (
    <div className="hub-screen">
      <ScreenHeader
        title={label}
        subtitle={subtitle}
        actions={
          <>
            <ImportExportMenu {...io} canExport={(snippets?.length ?? 0) > 0} />
            <Button variant="primary" icon="plus" onClick={openAdd}>
              Add snippet
            </Button>
          </>
        }
      />
      {editing?.original === null && editor}
      {snippets && snippets.length > 0 && (
        <section className="hub-list" aria-label="Your snippets">
          <ListHeading label={count(snippets.length, SNIPPET)} filter={filter} filterLabel="Filter snippets" onFilter={setFilter} />
          <div ref={list} className="hub-list-rows">
            {shown.map((snippet, i) => (
              <div key={snippet.trigger} data-flip-key={snippet.trigger}>
                {editing?.original === snippet.trigger ? (
                  editor
                ) : (
                  <ListRow
                    lead={
                      <>
                        <TriggerChip>{snippet.trigger}</TriggerChip>
                        <Icon name="arrowRight" size={16} className="hub-snippet-arrow" />
                      </>
                    }
                    primary={snippet.expansion.replaceAll("\n", " ")}
                    actions={
                      <>
                        <IconButton
                          icon="edit"
                          label={`Edit “${snippet.trigger}”`}
                          onClick={() => setEditing({ original: snippet.trigger, trigger: snippet.trigger, expansion: snippet.expansion, error: null })}
                        />
                        <IconButton icon="delete" label={`Remove “${snippet.trigger}”`} onClick={() => void remove(snippet)} />
                      </>
                    }
                  />
                )}
                {i < shown.length - 1 && <div className="vc-sep" />}
              </div>
            ))}
          </div>
          {shown.length === 0 && <NoMatch filter={filter} what="snippets" onClear={() => setFilter("")} />}
        </section>
      )}
      {snippets && snippets.length === 0 && editing?.original !== null && (
        <EmptyList art="snippets" title="No snippets yet" onImport={() => void importFile()} addLabel="Add snippet" onAdd={openAdd}>
          Snippets save typing for things you write often: an address, a sign-off, a meeting link. Say “my address” and the
          whole address appears.
        </EmptyList>
      )}
    </div>
  );
}

function SnippetEditor({ editing, onChange, onSave, onCancel }: {
  editing: Editing;
  onChange: (change: Partial<Editing>) => void;
  onSave: () => void;
  onCancel: () => void;
}) {
  const error = (field: SnippetField) => (editing.error?.field === field ? editing.error.message : undefined);
  return (
    <InlineEditor saveLabel="Save snippet" onSave={onSave} onCancel={onCancel}>
      <div className="hub-editor-trigger">
        <TextField
          label="When you say"
          value={editing.trigger}
          placeholder="e.g. my address"
          autoFocus
          maxLength={200}
          error={error("trigger")}
          onChange={(event) => onChange({ trigger: event.target.value, error: null })}
        />
      </div>
      <TextArea
        label="VoiceCtrl types"
        value={editing.expansion}
        placeholder="The full text"
        maxLength={4000}
        error={error("expansion")}
        onChange={(event) => onChange({ expansion: event.target.value, error: null })}
        onKeyDown={(event) => {
          // Enter makes a new line here; Ctrl+Enter saves, as in any multi-line field.
          if (event.key === "Enter" && event.ctrlKey) {
            event.preventDefault();
            onSave();
          }
        }}
      />
    </InlineEditor>
  );
}
