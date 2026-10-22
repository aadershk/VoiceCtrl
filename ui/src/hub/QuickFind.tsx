import { useId, useMemo, useRef, useState } from "react";
import type { KeyboardEvent } from "react";
import { SearchField } from "../design/components";
import { cx } from "../design/cx";
import { popoverEnter, popoverExit, usePresence } from "../design/motion";
import { searchQuickFind } from "./findIndex";
import type { FindEntry } from "./findIndex";

export interface QuickFindProps {
  onPick: (entry: FindEntry) => void;
}

/**
 * Quick Find (research 10): typing drops a results menu under the box, up to 8 screens and
 * Settings rows with where they live. Enter picks the highlighted one (the first, unless the
 * arrows moved it), Esc clears. A combobox for screen readers.
 */
export function QuickFind({ onPick }: QuickFindProps) {
  const [query, setQuery] = useState("");
  const [focused, setFocused] = useState(false);
  const [active, setActive] = useState(0);
  const input = useRef<HTMLInputElement>(null);
  const listId = useId();
  const hits = useMemo(() => searchQuickFind(query), [query]);

  const open = focused && query.trim() !== "";
  const { mounted, ref } = usePresence<HTMLDivElement>(open, {
    enter: (el) => popoverEnter(el, "top left"),
    exit: popoverExit,
  });
  // The menu keeps its last results while it fades out, rather than flashing "no results".
  const shown = useRef(hits);
  if (open) shown.current = hits;

  const pick = (entry: FindEntry) => {
    setQuery("");
    input.current?.blur();
    onPick(entry);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      if (hits.length === 0) return;
      event.preventDefault();
      const step = event.key === "ArrowDown" ? 1 : -1;
      setActive((at) => (at + step + hits.length) % hits.length);
    } else if (event.key === "Enter") {
      const entry = hits[active] ?? hits[0];
      if (entry) {
        event.preventDefault();
        pick(entry);
      }
    } else if (event.key === "Escape") {
      event.preventDefault();
      setQuery("");
      input.current?.blur();
    }
  };

  const optionId = (i: number) => `${listId}-${i}`;
  return (
    <div className="hub-quickfind">
      <SearchField
        ref={input}
        label="Quick Find"
        value={query}
        role="combobox"
        aria-expanded={open}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={open && hits.length > 0 ? optionId(active) : undefined}
        autoComplete="off"
        spellCheck={false}
        onChange={(event) => {
          setQuery(event.target.value);
          setActive(0);
        }}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        onKeyDown={onKeyDown}
      />
      {mounted && (
        <div ref={ref} id={listId} role="listbox" aria-label="Quick Find results" className="vc-menu hub-quickfind-results">
          {shown.current.map((entry, i) => (
            <div
              key={`${entry.screen}/${entry.row ?? ""}/${entry.label}`}
              id={optionId(i)}
              role="option"
              aria-selected={i === active}
              className={cx("hub-find-result", i === active && "hub-find-result-active")}
              // Mouse down, not click: the box's blur would close the menu before a click lands.
              onMouseDown={(event) => {
                event.preventDefault();
                pick(entry);
              }}
              onMouseEnter={() => setActive(i)}
            >
              <span className="hub-find-label">{entry.label}</span>
              <span className="hub-find-where">{entry.where}</span>
            </div>
          ))}
          {shown.current.length === 0 && <div className="hub-find-empty">No screen or setting by that name</div>}
        </div>
      )}
    </div>
  );
}
