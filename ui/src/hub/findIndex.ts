/**
 * Quick Find's index (CONTEXT: a navigation aid, not a content search): every Hub screen and
 * named Settings row, with aliases. Ported from v3's HubSearchIndex: case-insensitive substring
 * matching, and nothing for an empty query, since the sidebar already lists the screens.
 */
import { screens, settingsRows } from "./screens";
import type { ScreenId } from "./screens";

export interface FindEntry {
  label: string;
  /** Shown on the right of a result: "Screen", or the screen the row lives on. */
  where: string;
  screen: ScreenId;
  /** The row to land on, scroll to the centre and flash. */
  row?: string;
  aliases?: readonly string[];
}

export const MAX_RESULTS = 8;

export const findIndex: readonly FindEntry[] = [
  ...screens.map((s): FindEntry => ({ label: s.label, where: "Screen", screen: s.id })),
  ...settingsRows.filter((r) => r.findable !== false).map((r): FindEntry => ({
    label: r.label,
    where: "Settings",
    screen: "settings",
    row: r.id,
    ...(r.aliases ? { aliases: r.aliases } : {}),
  })),
  { label: "Import or export Dictionary", where: "Dictionary", screen: "dictionary", row: "import-export", aliases: ["csv"] },
  { label: "Import or export Snippets", where: "Snippets", screen: "snippets", row: "import-export", aliases: ["csv"] },
  {
    label: "Formal, Casual, Very casual or Excited",
    where: "Style",
    screen: "style",
    aliases: ["tone", "formal", "casual", "very casual", "excited", "exclamation", "lowercase", "contractions", "punctuation"],
  },
  { label: "Move an app to another kind", where: "Style", screen: "style", row: "move-apps", aliases: ["work chat", "personal chat", "email app", "add app"] },
  { label: "Your name", where: "Style", screen: "style", row: "your-name", aliases: ["sign-off", "signature", "email name"] },
];

function normalize(text: string): string {
  return text.trim().replace(/\s+/g, " ").toLowerCase();
}

/**
 * How well `text` matches `query`: 0 starts with it, 1 a word starts with it, 2 contains it,
 * null not at all.
 */
function tier(text: string, query: string): number | null {
  const t = text.toLowerCase();
  if (t.startsWith(query)) return 0;
  if (t.includes(` ${query}`)) return 1;
  return t.includes(query) ? 2 : null;
}

function rank(entry: FindEntry, query: string): number | null {
  const byLabel = tier(entry.label, query);
  if (byLabel !== null) return byLabel;
  // An alias hit ranks below any label hit, but still finds the row ("dark mode" → Theme).
  const byAlias = (entry.aliases ?? []).map((a) => tier(a, query)).filter((t) => t !== null);
  return byAlias.length > 0 ? 3 + Math.min(...byAlias) : null;
}

/** Up to `limit` entries for `query`, best first; ties keep the index order (screens first). */
export function searchQuickFind(query: string | null | undefined, index: readonly FindEntry[] = findIndex, limit = MAX_RESULTS): FindEntry[] {
  const q = normalize(query ?? "");
  if (q === "") return [];
  return index
    .map((entry, order) => ({ entry, order, score: rank(entry, q) }))
    .filter((hit): hit is { entry: FindEntry; order: number; score: number } => hit.score !== null)
    .sort((a, b) => a.score - b.score || a.order - b.order)
    .slice(0, limit)
    .map((hit) => hit.entry);
}
