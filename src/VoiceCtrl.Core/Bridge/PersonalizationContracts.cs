using VoiceCtrl.Core.Personalization;

namespace VoiceCtrl.Core.Bridge;

// The dictionary.* and snippets.* contracts (spec §5.3, research 10 "Dictionary" and "Snippets").
// Only the Hub registers them. Every command that changes a list answers with the list as it now
// is on disk, so the page never keeps its own copy in step by hand.

/// <summary>What an import did, for the toast. <see cref="Outcome"/> is <c>imported</c> even when
/// nothing new was added; the counts say what happened.</summary>
public sealed record ImportResult(
    ImportOutcome Outcome,
    int Added,
    int AlreadyThere,
    int NotAddedFull,
    IReadOnlyList<int> RejectedLines);

public enum ImportOutcome
{
    /// <summary>The file dialog was closed without a file.</summary>
    Cancelled,
    Imported,
    /// <summary>Not a VoiceCtrl CSV of this kind, or it couldn't be read. Nothing changed.</summary>
    Failed,
}

/// <param name="Folder">The folder's own name (e.g. "Downloads"), for "Exported 14 words to Downloads".</param>
public sealed record ExportResult(ExportOutcome Outcome, int Count, string Folder);

public enum ExportOutcome
{
    Cancelled,
    Exported,
    /// <summary>The file couldn't be written there (read-only, in use, no space).</summary>
    Failed,
}

// ---- dictionary.*

public sealed record DictionaryList(IReadOnlyList<DictionaryEntry> Entries);

[BridgeCommand("dictionary.list", Result = typeof(DictionaryList))]
public sealed record ListDictionary;

public sealed record DictionarySaveResult(DictionaryEditError Error, IReadOnlyList<DictionaryEntry> Entries);

/// <summary>Adds a word (<see cref="Original"/> null) or replaces the word called
/// <see cref="Original"/>. <see cref="AlsoHeardAs"/> is the field's text, phrases separated by commas.</summary>
[BridgeCommand("dictionary.save", Result = typeof(DictionarySaveResult))]
public sealed record SaveDictionaryWord(string? Original, string Word, string AlsoHeardAs);

/// <param name="Index">Where the word was, for Undo; -1 if it was already gone.</param>
public sealed record DictionaryRemoveResult(int Index, IReadOnlyList<DictionaryEntry> Entries);

[BridgeCommand("dictionary.remove", Result = typeof(DictionaryRemoveResult))]
public sealed record RemoveDictionaryWord(string Word);

/// <summary>Undo for <c>dictionary.remove</c>.</summary>
[BridgeCommand("dictionary.restore", Result = typeof(DictionaryList))]
public sealed record RestoreDictionaryWord(DictionaryEntry Entry, int Index);

/// <summary>Shows the Open dialog and imports the chosen CSV. The page then asks for the list.</summary>
[BridgeCommand("dictionary.import", Result = typeof(ImportResult))]
public sealed record ImportDictionary;

/// <summary>Shows the Save dialog (Downloads, "VoiceCtrl Dictionary.csv") and writes the CSV.</summary>
[BridgeCommand("dictionary.export", Result = typeof(ExportResult))]
public sealed record ExportDictionary;

// ---- snippets.*

public sealed record SnippetList(IReadOnlyList<Snippet> Snippets);

[BridgeCommand("snippets.list", Result = typeof(SnippetList))]
public sealed record ListSnippets;

public sealed record SnippetSaveResult(SnippetEditError Error, IReadOnlyList<Snippet> Snippets);

/// <summary>Adds a Snippet (<see cref="Original"/> null) or replaces the one whose trigger is
/// <see cref="Original"/>.</summary>
[BridgeCommand("snippets.save", Result = typeof(SnippetSaveResult))]
public sealed record SaveSnippet(string? Original, string Trigger, string Expansion);

public sealed record SnippetRemoveResult(int Index, IReadOnlyList<Snippet> Snippets);

[BridgeCommand("snippets.remove", Result = typeof(SnippetRemoveResult))]
public sealed record RemoveSnippet(string Trigger);

[BridgeCommand("snippets.restore", Result = typeof(SnippetList))]
public sealed record RestoreSnippet(Snippet Snippet, int Index);

[BridgeCommand("snippets.import", Result = typeof(ImportResult))]
public sealed record ImportSnippets;

[BridgeCommand("snippets.export", Result = typeof(ExportResult))]
public sealed record ExportSnippets;

// ---- dictionary suggestions (slice 24)

/// <summary>The Suggestions card, freshly mined. May take a moment when TypeSafe judges new words.</summary>
[BridgeCommand("dictionary.suggestions", Result = typeof(SuggestionsView))]
public sealed record GetDictionarySuggestions;

/// <summary>The chip's Add. Answers like <c>dictionary.save</c>, with the list as it now is.</summary>
[BridgeCommand("dictionary.addSuggestion", Result = typeof(DictionarySaveResult))]
public sealed record AddDictionarySuggestion(string Word);

/// <summary>The chip's Ignore: the word is never offered again.</summary>
[BridgeCommand("dictionary.ignoreSuggestion")]
public sealed record IgnoreDictionarySuggestion(string Word);
