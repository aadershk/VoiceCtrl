/**
 * What the Dictionary and Snippets screens share (research 10): the "Import or export" menu in
 * the header, the count heading with its Filter, and the no-match line.
 */
import { useState } from "react";
import type { ReactNode } from "react";
import type { ExportResult, ImportResult } from "../../bridge/contracts.g";
import { ArtTile, Button, EmptyState, Menu, MenuAnchor, MenuItem, SearchField } from "../../design/components";
import { cx } from "../../design/cx";
import { useHub } from "../hubContext";
import { exportToast, importToast } from "../personalizationCopy";
import type { Noun } from "../personalizationCopy";
import { rowDomId } from "../screens";

/** Quick Find's "Import or export Dictionary/Snippets" lands here (screens.ts, findIndex.ts). */
export const IMPORT_EXPORT_ROW = "import-export";

export interface ImportExportProps {
  noun: Noun;
  /** "the Dictionary", for "5 didn't fit: the Dictionary holds 200." */
  listName: string;
  canExport: boolean;
  onImport: () => Promise<ImportResult>;
  onExport: () => Promise<ExportResult>;
  /** Runs after a successful import, to show the new list. */
  onImported: () => void;
}

/** Runs an import or export and says how it went. Shared by the menu and the empty state's Import. */
export function useImportExport({ noun, listName, onImport, onExport, onImported }: Omit<ImportExportProps, "canExport">) {
  const { toast } = useHub();
  const say = (said: { message: string; error: boolean } | null) => {
    if (said) toast(said.message, { error: said.error });
  };
  return {
    importFile: async () => {
      const result = await onImport();
      if (result.outcome === "imported") onImported();
      say(importToast(result, noun, listName));
    },
    exportFile: async () => say(exportToast(await onExport(), noun)),
  };
}

export function ImportExportMenu(props: ImportExportProps) {
  const { isLanding } = useHub();
  const [open, setOpen] = useState(false);
  const { importFile, exportFile } = useImportExport(props);
  return (
    <span id={rowDomId(IMPORT_EXPORT_ROW)} className={cx("hub-landing", isLanding(IMPORT_EXPORT_ROW) && "hub-landing-flash")}>
      <MenuAnchor>
        <Button icon="import" aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen((o) => !o)}>
          Import or export
        </Button>
        <Menu open={open} onClose={() => setOpen(false)} label="Import or export" placement="below-end">
          <MenuItem icon="import" onSelect={() => void importFile()}>
            Import from a file
          </MenuItem>
          <MenuItem icon="export" disabled={!props.canExport} onSelect={() => void exportFile()}>
            Export to a file
          </MenuItem>
        </Menu>
      </MenuAnchor>
    </span>
  );
}

export interface ListHeadingProps {
  label: string;
  filter: string;
  filterLabel: string;
  onFilter: (value: string) => void;
}

/** "14 words" on the left, a small Filter field on the right. */
export function ListHeading({ label, filter, filterLabel, onFilter }: ListHeadingProps) {
  return (
    <div className="hub-list-heading">
      <h2 className="vc-group">{label}</h2>
      <SearchField
        className="hub-filter"
        label={filterLabel}
        placeholder="Filter"
        value={filter}
        onChange={(event) => onFilter(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Escape" && filter) {
            event.stopPropagation();
            onFilter("");
          }
        }}
      />
    </div>
  );
}

export function NoMatch({ filter, what, onClear }: { filter: string; what: string; onClear: () => void }) {
  return (
    <EmptyState
      art={<ArtTile art="search" />}
      title={`No ${what} match “${filter.trim()}”`}
      actions={<Button onClick={onClear}>Clear filter</Button>}
    >
      Filter looks through everything on this list.
    </EmptyState>
  );
}

export function EmptyList({ art, title, children, onImport, addLabel, onAdd }: {
  art: "dictionary" | "snippets";
  title: string;
  children: ReactNode;
  onImport: () => void;
  addLabel: string;
  onAdd: () => void;
}) {
  return (
    <EmptyState
      art={<ArtTile art={art} />}
      title={title}
      actions={
        <>
          <Button icon="import" onClick={onImport}>
            Import
          </Button>
          <Button variant="primary" icon="plus" onClick={onAdd}>
            {addLabel}
          </Button>
        </>
      }
    >
      {children}
    </EmptyState>
  );
}

/** Case-insensitive "contains" over any of the texts. */
export function matches(filter: string, ...texts: readonly string[]): boolean {
  const needle = filter.trim().toLocaleLowerCase();
  return needle.length === 0 || texts.some((text) => text.toLocaleLowerCase().includes(needle));
}
