/**
 * Every line the Dictionary and Snippets screens say (research 10 "Dictionary", "Snippets",
 * "States"), kept out of the components so each can be tested on its own.
 */
import type { DictionaryEditError, ExportResult, ImportResult, SnippetEditError } from "../bridge/contracts.g";

/** Matches Core's CustomDictionary.MaxTermLength, MaxTerms and MaxAlsoHeardAs. */
const MAX_TERM = 64;
const MAX_WORDS = 200;
const MAX_HEARD_AS = 5;
/** Matches Core's PersonalizationLibrary.MaxTriggerLength and MaxExpansionLength, SnippetTable.MaxSnippets. */
const MAX_TRIGGER = 64;
const MAX_EXPANSION = 2000;
const MAX_SNIPPETS = 200;

const number = new Intl.NumberFormat("en-US");

export interface Noun {
  one: string;
  many: string;
  /** How many the list holds at most. */
  limit: number;
}

export const WORD: Noun = { one: "word", many: "words", limit: MAX_WORDS };
export const SNIPPET: Noun = { one: "snippet", many: "snippets", limit: MAX_SNIPPETS };

/** "1 word", "14 words". */
export function count(n: number, noun: Noun): string {
  return `${number.format(n)} ${n === 1 ? noun.one : noun.many}`;
}

export type WordField = "word" | "alsoHeardAs";

/** Which field a Dictionary error sits under, and what it says. */
export function wordError(error: DictionaryEditError, word: string): { field: WordField; message: string } | null {
  switch (error) {
    case "none":
      return null;
    case "wordMissing":
      return { field: "word", message: "Type the word first." };
    case "wordTooLong":
      return { field: "word", message: `Keep it to ${MAX_TERM} characters or fewer.` };
    case "wordBadCharacter":
      return { field: "word", message: "A word can't start with # or contain |." };
    case "alreadyThere":
      return { field: "word", message: `${word.trim()} is already in your Dictionary.` };
    case "full":
      return { field: "word", message: `Your Dictionary is full at ${number.format(MAX_WORDS)} words. Remove one to add another.` };
    case "heardAsTooLong":
      return { field: "alsoHeardAs", message: `Keep each one to ${MAX_TERM} characters or fewer.` };
    case "heardAsTooMany":
      return { field: "alsoHeardAs", message: `Up to ${MAX_HEARD_AS}, separated by commas.` };
    case "heardAsBadCharacter":
      return { field: "alsoHeardAs", message: "Leave out the | character." };
  }
}

export type SnippetField = "trigger" | "expansion";

export function snippetError(error: SnippetEditError, trigger: string): { field: SnippetField; message: string } | null {
  switch (error) {
    case "none":
      return null;
    case "triggerMissing":
      return { field: "trigger", message: "Type what you'll say first." };
    case "triggerTooLong":
      return { field: "trigger", message: `Keep it to ${MAX_TRIGGER} characters or fewer.` };
    case "triggerBadCharacter":
      return { field: "trigger", message: "A trigger can't start with # or contain =." };
    case "alreadyThere":
      return { field: "trigger", message: `“${trigger.trim()}” is already a snippet.` };
    case "full":
      return {
        field: "trigger",
        message: `You have ${number.format(MAX_SNIPPETS)} snippets, the most VoiceCtrl keeps. Remove one to add another.`,
      };
    case "expansionTooLong":
      return { field: "expansion", message: `Keep it to ${number.format(MAX_EXPANSION)} characters or fewer.` };
  }
}

/** The one row line under a Dictionary word. */
export function wordMeta(alsoHeardAs: readonly string[]): string {
  return alsoHeardAs.length > 0 ? `also heard as “${alsoHeardAs.join(", ")}”` : "spelled exactly like this";
}

export const IMPORT_FAILED = "That file couldn't be imported. Choose a .csv file exported from VoiceCtrl.";
export const EXPORT_FAILED = "That file couldn't be saved there. Try another folder.";

/** Line numbers as "4", "4 and 9", "4, 9 and 12", or the first three and how many more. */
function lineList(lines: readonly number[]): string {
  const shown = lines.slice(0, 3).map((n) => number.format(n));
  const more = lines.length - shown.length;
  if (more > 0) return `${shown.join(", ")} and ${more} more`;
  if (shown.length === 1) return shown[0] ?? "";
  return `${shown.slice(0, -1).join(", ")} and ${shown[shown.length - 1]}`;
}

/**
 * The toast after an import, or null when the dialog was cancelled. The board's line is
 * "Imported 12 words. 3 were already there."; the rest only appear when they apply.
 */
export function importToast(result: ImportResult, noun: Noun, listName: string): { message: string; error: boolean } | null {
  if (result.outcome === "cancelled") return null;
  if (result.outcome === "failed") return { message: IMPORT_FAILED, error: true };

  const parts = [result.added > 0 ? `Imported ${count(result.added, noun)}.` : "Nothing new to import."];
  if (result.alreadyThere > 0) {
    parts.push(`${number.format(result.alreadyThere)} ${result.alreadyThere === 1 ? "was" : "were"} already there.`);
  }
  if (result.notAddedFull > 0) {
    parts.push(`${number.format(result.notAddedFull)} didn't fit: ${listName} holds ${number.format(noun.limit)}.`);
  }
  if (result.rejectedLines.length > 0) {
    const lines = result.rejectedLines.length === 1 ? "Line" : "Lines";
    parts.push(`${lines} ${lineList(result.rejectedLines)} couldn't be read.`);
  }
  return { message: parts.join(" "), error: false };
}

export function exportToast(result: ExportResult, noun: Noun): { message: string; error: boolean } | null {
  if (result.outcome === "cancelled") return null;
  if (result.outcome === "failed") return { message: EXPORT_FAILED, error: true };
  const where = result.folder ? ` to ${result.folder}` : "";
  return { message: `Exported ${count(result.count, noun)}${where}`, error: false };
}
