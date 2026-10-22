using System.Windows.Forms;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Personalization;

namespace VoiceCtrl.Hub;

/// <summary>
/// The Hub's <c>dictionary.*</c> and <c>snippets.*</c> commands (spec §5.3). The rules live in
/// <see cref="PersonalizationLibrary"/>; this adds what only the host can do, the Open and Save
/// dialogs, since the page never touches files.
/// </summary>
internal sealed class PersonalizationCommands(PersonalizationLibrary library, Func<IWin32Window?> owner)
{
    private const string CsvFilter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";

    public void Register(BridgeDispatcher dispatcher)
    {
        dispatcher.Handle<ListDictionary, DictionaryList>(_ => Task.FromResult(new DictionaryList(library.Dictionary())));

        dispatcher.Handle<SaveDictionaryWord, DictionarySaveResult>(args =>
        {
            DictionaryEditError error = library.SaveWord(args.Original, Required(args.Word), args.AlsoHeardAs ?? string.Empty);
            return Task.FromResult(new DictionarySaveResult(error, library.Dictionary()));
        });

        dispatcher.Handle<RemoveDictionaryWord, DictionaryRemoveResult>(args =>
        {
            int index = library.RemoveWord(Required(args.Word));
            return Task.FromResult(new DictionaryRemoveResult(index, library.Dictionary()));
        });

        dispatcher.Handle<RestoreDictionaryWord, DictionaryList>(args =>
        {
            DictionaryEntry entry = args.Entry ?? throw BadArgs();
            library.RestoreWord(new DictionaryEntry(Required(entry.Word), entry.AlsoHeardAs ?? []), args.Index);
            return Task.FromResult(new DictionaryList(library.Dictionary()));
        });

        dispatcher.Handle<ImportDictionary, ImportResult>(_ => ImportAsync("Import into your Dictionary", library.ImportDictionary));

        dispatcher.Handle<ExportDictionary, ExportResult>(_ =>
            ExportAsync("Export your Dictionary", "VoiceCtrl Dictionary.csv", library.Dictionary().Count, library.ExportDictionary));

        dispatcher.Handle<ListSnippets, SnippetList>(_ => Task.FromResult(new SnippetList(library.Snippets())));

        dispatcher.Handle<SaveSnippet, SnippetSaveResult>(args =>
        {
            SnippetEditError error = library.SaveSnippet(args.Original, Required(args.Trigger), args.Expansion ?? string.Empty);
            return Task.FromResult(new SnippetSaveResult(error, library.Snippets()));
        });

        dispatcher.Handle<RemoveSnippet, SnippetRemoveResult>(args =>
        {
            int index = library.RemoveSnippet(Required(args.Trigger));
            return Task.FromResult(new SnippetRemoveResult(index, library.Snippets()));
        });

        dispatcher.Handle<RestoreSnippet, SnippetList>(args =>
        {
            Snippet snippet = args.Snippet ?? throw BadArgs();
            library.RestoreSnippet(new Snippet(Required(snippet.Trigger), snippet.Expansion ?? string.Empty), args.Index);
            return Task.FromResult(new SnippetList(library.Snippets()));
        });

        dispatcher.Handle<ImportSnippets, ImportResult>(_ => ImportAsync("Import Snippets", library.ImportSnippets));

        dispatcher.Handle<ExportSnippets, ExportResult>(_ =>
            ExportAsync("Export your Snippets", "VoiceCtrl Snippets.csv", library.Snippets().Count, library.ExportSnippets));
    }

    private async Task<ImportResult> ImportAsync(string title, Func<byte[], ImportReport> import)
    {
        // Leave WebView2's message handler before a modal dialog runs its own message loop inside it.
        await Task.Yield();

        using var dialog = new OpenFileDialog { Title = title, Filter = CsvFilter, InitialDirectory = DownloadsFolder() };
        if (dialog.ShowDialog(owner()) != DialogResult.OK)
        {
            return new ImportResult(ImportOutcome.Cancelled, 0, 0, 0, []);
        }

        try
        {
            var file = new FileInfo(dialog.FileName);
            if (file.Length > PersonalizationCsv.MaxFileBytes)
            {
                return new ImportResult(ImportOutcome.Failed, 0, 0, 0, []);
            }

            ImportReport report = import(await File.ReadAllBytesAsync(dialog.FileName));
            SimpleFileLogger.LogInfo(
                $"Import: readable={report.Readable} added={report.Added} alreadyThere={report.AlreadyThere} " +
                $"full={report.NotAddedFull} rejected={report.RejectedLines.Count}");

            return report.Readable
                ? new ImportResult(ImportOutcome.Imported, report.Added, report.AlreadyThere, report.NotAddedFull, report.RejectedLines)
                : new ImportResult(ImportOutcome.Failed, 0, 0, 0, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SimpleFileLogger.LogError("Import", ex);
            return new ImportResult(ImportOutcome.Failed, 0, 0, 0, []);
        }
    }

    private async Task<ExportResult> ExportAsync(string title, string fileName, int count, Func<byte[]> export)
    {
        await Task.Yield();

        using var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = CsvFilter,
            FileName = fileName,
            DefaultExt = "csv",
            InitialDirectory = DownloadsFolder(),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(owner()) != DialogResult.OK)
        {
            return new ExportResult(ExportOutcome.Cancelled, 0, string.Empty);
        }

        string folder = Path.GetFileName(Path.GetDirectoryName(dialog.FileName)) ?? string.Empty;
        try
        {
            await File.WriteAllBytesAsync(dialog.FileName, export());
            return new ExportResult(ExportOutcome.Exported, count, folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SimpleFileLogger.LogError("Export", ex);
            return new ExportResult(ExportOutcome.Failed, 0, folder);
        }
    }

    /// <summary>%USERPROFILE%\Downloads, where Windows puts it unless the user moved it; a moved
    /// one simply opens the dialog wherever Windows last left it.</summary>
    private static string DownloadsFolder()
    {
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(downloads) ? downloads : string.Empty;
    }

    // The page is a system boundary (spec §4): a missing string is a bad call, not a crash.
    private static string Required(string? value) => value ?? throw BadArgs();

    private static BridgeCommandException BadArgs() => new("bad_args", "Missing argument.");
}
