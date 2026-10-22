import { useEffect, useRef, useState } from "react";
import type { DictionaryEntry, DictionarySuggestion, SuggestionsView } from "../../bridge/contracts.g";
import { Button, Icon, IconButton } from "../../design/components";
import { enter, exit, useFlipList, usePresence } from "../../design/motion";
import { useHub } from "../hubContext";
import { wordError } from "../personalizationCopy";

export const SUGGESTIONS_ON = "Suggestions are on. New ones appear here.";

/** "1 suggestion", "3 suggestions". */
export function suggestionTitle(n: number): string {
  return n === 1 ? "1 suggestion" : `${n} suggestions`;
}

/**
 * Dictionary Suggestions on the Dictionary screen (research 10 "Dictionary"): with the setting on,
 * a card of chips ("Maya Ortiz 6×", Add, Ignore) for words found in the kept Dictations; with it
 * off, the dashed Turn on card. Nothing shows in Offline mode, where none are made, or when there
 * is nothing to suggest. A chip for a word the list already has (added by hand meanwhile) hides.
 */
export function SuggestionsCard({ entries, onEntries }: {
  entries: readonly DictionaryEntry[] | null;
  onEntries: (entries: DictionaryEntry[]) => void;
}) {
  const { bridge, toast } = useHub();
  const [view, setView] = useState<SuggestionsView | null>(null);
  const [gone, setGone] = useState<ReadonlySet<string>>(new Set());
  const chips = useRef<HTMLDivElement>(null);

  const load = () => void bridge.call("dictionary.suggestions", {}).then(setView, () => undefined);
  useEffect(load, [bridge]);

  const inList = new Set((entries ?? []).map((e) => e.word.toLocaleLowerCase()));
  const shown = (view?.items ?? []).filter((s) => !gone.has(s.word) && !inList.has(s.word.toLocaleLowerCase()));
  useFlipList(chips, [shown.length]);

  const card = usePresence<HTMLElement>(!!view?.on && !view.offline && shown.length > 0, { enter, exit });
  const offCard = usePresence<HTMLElement>(!!view && !view.on, { enter, exit });

  const leave = async (word: string) => {
    const chip = chips.current?.querySelector<HTMLElement>(`[data-flip-key="${CSS.escape(word)}"]`);
    // The last chip leaves with its card.
    if (chip && shown.length > 1) await exit(chip);
    setGone((g) => new Set(g).add(word));
  };

  const add = async (suggestion: DictionarySuggestion) => {
    try {
      const { error, entries: after } = await bridge.call("dictionary.addSuggestion", { word: suggestion.word });
      const problem = error === "alreadyThere" ? null : wordError(error, suggestion.word);
      if (problem) {
        toast(problem.message, { error: true });
        return;
      }
      await leave(suggestion.word);
      onEntries(after);
      if (error === "none") toast(`Added ${suggestion.word} to your Dictionary`);
    } catch {
      toast("That couldn't be saved. Try again.", { error: true });
    }
  };

  const ignore = async (suggestion: DictionarySuggestion) => {
    await leave(suggestion.word);
    void bridge.call("dictionary.ignoreSuggestion", { word: suggestion.word }).catch(() => undefined);
  };

  const turnOn = async () => {
    try {
      await bridge.call("settings.setSuggestWords", { on: true });
      toast(SUGGESTIONS_ON);
      load();
    } catch {
      toast("That couldn't be saved. Try again.", { error: true });
    }
  };

  return (
    <>
      {card.mounted && (
        <section ref={card.ref} className="hub-suggest" aria-label="Suggestions">
          <div className="hub-suggest-head">
            <Icon name="sparkle" size={16} />
            <span className="hub-suggest-title">{suggestionTitle(Math.max(shown.length, 1))}</span>
            <span className="hub-suggest-hint">Found in your Dictations. Nothing is added until you say so.</span>
          </div>
          <div ref={chips} className="hub-suggest-chips">
            {shown.map((s) => (
              <span key={s.word} data-flip-key={s.word} className="hub-suggest-chip">
                {s.word}
                <span className="hub-suggest-count" aria-label={`said in ${s.count} Dictations`}>
                  {s.count}×
                </span>
                <IconButton size="sm" icon="check" label={`Add ${s.word}`} onClick={() => void add(s)} />
                <IconButton size="sm" icon="close" label={`Ignore ${s.word}`} onClick={() => void ignore(s)} />
              </span>
            ))}
          </div>
        </section>
      )}
      {offCard.mounted && (
        <section ref={offCard.ref} className="hub-suggest-off" aria-label="Suggestions">
          <Icon name="sparkle" size={16} />
          <span className="hub-suggest-off-text">
            VoiceCtrl can suggest names and words you say often. Off, and never used in Offline mode.
          </span>
          <Button size="sm" onClick={() => void turnOn()}>
            Turn on
          </Button>
        </section>
      )}
    </>
  );
}
