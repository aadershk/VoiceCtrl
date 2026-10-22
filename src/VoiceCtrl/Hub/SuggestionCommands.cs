using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Web;

namespace VoiceCtrl.Hub;

/// <summary>
/// Dictionary Suggestions (slice 24): the Dictionary screen's <c>dictionary.suggestions</c> card and
/// Settings' Word suggestions rows. The rules live in <see cref="DictionarySuggestions"/>; this only
/// answers the page and pushes <c>settings.suggestions</c> when the rows change. Lives as long as
/// the app; the Hub is attached while it is open.
/// </summary>
internal sealed class SuggestionCommands : IDisposable
{
    private readonly DictionarySuggestions _suggestions;
    private readonly SynchronizationContext _ui;
    private SurfaceForm? _hub;

    public SuggestionCommands(DictionarySuggestions suggestions, SynchronizationContext ui)
    {
        _suggestions = suggestions;
        _ui = ui;
        _suggestions.Changed += Push;
    }

    public void Register(BridgeDispatcher dispatcher)
    {
        dispatcher.Handle<GetDictionarySuggestions, SuggestionsView>(_ => _suggestions.GetAsync());

        dispatcher.Handle<AddDictionarySuggestion, DictionarySaveResult>(args =>
        {
            DictionaryEditError error = _suggestions.Add(Required(args.Word));
            return Task.FromResult(new DictionarySaveResult(error, _suggestions.Dictionary()));
        });

        dispatcher.Handle<IgnoreDictionarySuggestion>(args =>
        {
            _suggestions.Ignore(Required(args.Word));
            return Task.CompletedTask;
        });

        dispatcher.Handle<GetSuggestionSettings, SuggestionSettingsView>(args =>
        {
            // A key from an earlier run is checked once, so the row can say "Works"; the answer arrives as an event.
            _ = VerifyKeyAsync();
            return Task.FromResult(_suggestions.SettingsView());
        });

        dispatcher.Handle<SetSuggestWords>(args =>
        {
            _suggestions.SetOn(args.On);
            return Task.CompletedTask;
        });

        dispatcher.Handle<SaveTypeSafeKey, KeyCheckResult>(async args =>
        {
            try
            {
                return new KeyCheckResult(await _suggestions.SaveKeyAsync(Required(args.Key)));
            }
            catch (ArgumentException ex)
            {
                throw new BridgeCommandException("bad_args", ex.Message);
            }
        });

        dispatcher.Handle<RemoveTypeSafeKey>(_ =>
        {
            _suggestions.RemoveKey();
            return Task.CompletedTask;
        });
    }

    /// <summary>The Hub opened: its page gets this group's events until it closes.</summary>
    public void Attach(SurfaceForm hub)
    {
        _hub = hub;
        hub.FormClosed += (_, _) =>
        {
            if (_hub == hub)
            {
                _hub = null;
            }
        };
    }

    private async Task VerifyKeyAsync()
    {
        try
        {
            await _suggestions.VerifySavedKeyAsync();
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("TypeSafe key check", ex);
        }
    }

    // Changes arrive on network threads too; the page reads the view as it is when the post runs.
    private void Push() => _ui.Post(_ => _hub?.Post(new SuggestionSettingsChanged(_suggestions.SettingsView())), null);

    // The page is a system boundary (spec §4): a missing string is a bad call, not a crash.
    private static string Required(string? value) => value ?? throw new BridgeCommandException("bad_args", "Missing argument.");

    public void Dispose() => _suggestions.Changed -= Push;
}
