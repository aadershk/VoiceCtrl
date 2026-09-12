using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using VoiceCtrl.Hub.Controls;

namespace VoiceCtrl.Hub;

/// <summary>
/// The Hub: opened from the tray, lazy-created and torn down on close rather than hidden and
/// reused, matching CONTEXT.md's Hub definition ("Only exists in memory while open"). Dictations is
/// wired to <see cref="DictationHistoryStore"/> (ticket 06); Dictionary (07), Snippets (08), and
/// Profiles (09) read/write their files directly.
/// </summary>
public partial class HubWindow : Window
{
    private readonly DictationHistoryStore _history;
    private readonly TranscriptionModeStore _modeStore;
    private readonly string _envPath;

    private string _savedApiKey = string.Empty;
    private bool _apiKeyRevealed;
    private bool _apiKeyConfirmedInvalid;

    /// <summary>Set right before <see cref="ConfirmDialogOverlay"/> is shown, invoked if the user
    /// confirms, cleared either way. Shared across sections so later editor tickets (07/08/09) can
    /// reuse this same overlay for their own destructive actions instead of building their own.</summary>
    private Action? _pendingConfirmAction;

    private readonly List<string> _dictionaryTerms = [];
    private string _dictionaryHeader = string.Empty;
    private bool _dictionaryAdding;
    private int? _dictionaryEditingIndex;

    private readonly List<KeyValuePair<string, string>> _snippets = [];
    private string _snippetsHeader = string.Empty;
    private bool _snippetAdding;
    private int? _snippetEditingIndex;

    private readonly List<KeyValuePair<string, AppProfile>> _profiles = [];
    private IReadOnlyList<string> _profilesComment = [];
    private bool _profileAdding;
    private int? _profileEditingIndex;

    private HotkeySettings _hotkeySettings = new();
    private bool _hotkeyChooserOpen;
    private bool _triggerChooserOpen;
    private bool _chordListening;

    private static readonly (string Label, string? Value)[] FormattingOptions =
    [
        ("Default (built-in)", null),
        ("None", AppProfile.None),
        ("Structured", AppProfile.Structured),
        ("Prose", AppProfile.Prose),
    ];

    private static readonly (string Label, string? Value)[] CleanupOptions =
    [
        ("Default (built-in)", null),
        ("Light", "light"),
        ("Standard", "standard"),
        ("Aggressive", "aggressive"),
    ];

    public HubWindow(DictationHistoryStore history, TranscriptionModeStore modeStore, string envPath)
    {
        _history = history;
        _modeStore = modeStore;
        _envPath = envPath;
        InitializeComponent();
        VersionText.Text = "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");

        // Set after InitializeComponent, not via IsChecked="True" in XAML: the Checked handler
        // below reaches into the other named panels, and BAML assigns x:Name fields in document
        // order, so firing it mid-parse would touch panels declared later that aren't wired yet.
        DictationsNavButton.IsChecked = true;
        HotkeySubtabButton.IsChecked = true; // same reasoning, for SettingsSubtab_Checked below

        RefreshDictationsList();

        // A Dictation completed via the Overlay while the Hub is already open otherwise never
        // shows up until the Hub is closed and reopened — Dictations is the one screen backed by a
        // live, in-memory store rather than a file re-read on tab visit. Unsubscribed on Closed so
        // a torn-down Hub doesn't leak into the store's invocation list for the next one.
        _history.Changed += OnHistoryChanged;
        Closed += (_, _) => _history.Changed -= OnHistoryChanged;
    }

    private void OnHistoryChanged() => RefreshDictationsList();

    private void NavButton_Checked(object sender, RoutedEventArgs e)
    {
        var tag = (string)((RadioButton)sender).Tag;
        DictationsPanel.Visibility = tag == "Dictations" ? Visibility.Visible : Visibility.Collapsed;
        DictionaryPanel.Visibility = tag == "Dictionary" ? Visibility.Visible : Visibility.Collapsed;
        SnippetsPanel.Visibility = tag == "Snippets" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesPanel.Visibility = tag == "Profiles" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;

        if (tag == "Dictionary")
        {
            // Reloaded on every visit, not just once at Hub startup: the tray's "Dictionary..."
            // entry still opens the same file in Notepad (CONTEXT.md's Hub definition says the two
            // coexist), so this is the only way the Hub notices a hand-edit made that way.
            _dictionaryAdding = false;
            _dictionaryEditingIndex = null;
            LoadDictionaryFile();
            RefreshDictionaryList();
        }
        else if (tag == "Snippets")
        {
            // Same reload-on-visit reasoning as Dictionary above: the tray's Personalize > Snippets
            // entry still opens snippets.txt directly in Notepad.
            _snippetAdding = false;
            _snippetEditingIndex = null;
            LoadSnippetsFile();
            RefreshSnippetsList();
        }
        else if (tag == "Profiles")
        {
            // Same reload-on-visit reasoning as Dictionary/Snippets above: profiles.json can still
            // be hand-edited directly, and the live transcription pipeline picks up either kind of
            // edit itself via PersonalizationStore's own stat-based cache.
            _profileAdding = false;
            _profileEditingIndex = null;
            LoadProfilesFile();
            RefreshProfilesList();
        }
        else if (tag == "Settings")
        {
            // Same reload-on-visit reasoning as Dictionary/Snippets/Profiles above: the tray's
            // "Settings..." entry still opens .env directly in Notepad.
            LoadTranscriptionSettings();

            _hotkeySettings = HotkeySettings.Load();
            _hotkeyChooserOpen = false;
            _triggerChooserOpen = false;
            _chordListening = false;
            RefreshHotkeyPanel();
        }
    }

    private void SettingsSubtab_Checked(object sender, RoutedEventArgs e)
    {
        var tag = (string)((RadioButton)sender).Tag;
        HotkeySubtabPanel.Visibility = tag == "Hotkey" ? Visibility.Visible : Visibility.Collapsed;
        TranscriptionSubtabPanel.Visibility = tag == "Transcription" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadTranscriptionSettings()
    {
        _savedApiKey = ConfigLoader.Load(_envPath).GeminiApiKey;
        _apiKeyRevealed = false;
        _apiKeyConfirmedInvalid = false;
        ApiKeyStatusText.Visibility = Visibility.Collapsed;
        SetApiKeyBoxes(_savedApiKey);
        UpdateApiKeyVisibility();
        RefreshApiKeySaveButtonState();
        RefreshModeButtons();
    }

    private void RefreshModeButtons()
    {
        ModeAutoButton.IsChecked = _modeStore.Current == TranscriptionModePreference.Auto;
        ModeOnlineButton.IsChecked = _modeStore.Current == TranscriptionModePreference.Online;
        ModeOfflineButton.IsChecked = _modeStore.Current == TranscriptionModePreference.Offline;
    }

    /// <summary>Mirrors TrayIconManager.SelectMode exactly: the Hub's Mode control and the tray's
    /// Mode menu are two views onto the same live TranscriptionModeStore, so a Dictation picks up
    /// either one's choice immediately, no restart needed.</summary>
    private void ModeButton_Checked(object sender, RoutedEventArgs e)
    {
        var tag = (string)((RadioButton)sender).Tag;
        if (!Enum.TryParse(tag, out TranscriptionModePreference mode) || _modeStore.Current == mode)
        {
            return;
        }

        _modeStore.Current = mode;
        _modeStore.Save();
    }

    private void SetApiKeyBoxes(string value)
    {
        ApiKeyPasswordBox.Password = value;
        ApiKeyTextBox.Text = value;
    }

    private string CurrentApiKeyBoxText() => _apiKeyRevealed ? ApiKeyTextBox.Text : ApiKeyPasswordBox.Password;

    private void UpdateApiKeyVisibility()
    {
        ApiKeyPasswordBox.Visibility = _apiKeyRevealed ? Visibility.Collapsed : Visibility.Visible;
        ApiKeyTextBox.Visibility = _apiKeyRevealed ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyEyeIcon.Data = (Geometry)FindResource(_apiKeyRevealed ? "Icon.EyeOff" : "Icon.Eye");
    }

    private void ApiKeyEyeButton_Click(object sender, RoutedEventArgs e)
    {
        string current = CurrentApiKeyBoxText();
        _apiKeyRevealed = !_apiKeyRevealed;
        SetApiKeyBoxes(current);
        UpdateApiKeyVisibility();

        if (_apiKeyRevealed)
        {
            ApiKeyTextBox.Focus();
            ApiKeyTextBox.CaretIndex = ApiKeyTextBox.Text.Length;
        }
        else
        {
            ApiKeyPasswordBox.Focus();
        }
    }

    private void ApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => OnApiKeyBoxChanged();

    private void ApiKeyTextBox_TextChanged(object sender, TextChangedEventArgs e) => OnApiKeyBoxChanged();

    private void OnApiKeyBoxChanged()
    {
        _apiKeyConfirmedInvalid = false;
        ApiKeyStatusText.Visibility = Visibility.Collapsed;
        RefreshApiKeySaveButtonState();
    }

    private void RefreshApiKeySaveButtonState() =>
        ApiKeySaveButton.IsEnabled = CurrentApiKeyBoxText().Trim() != _savedApiKey;

    private void ApiKeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ApiKeySaveButton.IsEnabled)
        {
            ApiKeySaveButton_Click(ApiKeySaveButton, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    /// <summary>Mirrors SetupWindow's OnOnlineContinueClicked: an invalid key blocks the first Save
    /// with a warning rather than silently writing it, but a second click (after the user has seen
    /// the warning) goes through anyway, since AdaptiveTranscriptionClient already tolerates a bad
    /// key gracefully at runtime. A network error/timeout during the check never blocks saving.</summary>
    private async void ApiKeySaveButton_Click(object sender, RoutedEventArgs e)
    {
        string key = CurrentApiKeyBoxText().Trim();
        if (key == _savedApiKey)
        {
            return;
        }

        if (key.Length > 0 && !_apiKeyConfirmedInvalid)
        {
            ApiKeySaveButton.IsEnabled = false;
            ApiKeyStatusText.Foreground = (Brush)FindResource("HubInkDimBrush");
            ApiKeyStatusText.Text = "Checking key...";
            ApiKeyStatusText.Visibility = Visibility.Visible;

            GeminiApiKeyValidator.Result result = await GeminiApiKeyValidator.ValidateAsync(key);

            RefreshApiKeySaveButtonState();

            if (result == GeminiApiKeyValidator.Result.InvalidKey)
            {
                ApiKeyStatusText.Foreground = (Brush)FindResource("HubDangerBrush");
                ApiKeyStatusText.Text = "That key doesn't look right. Double-check it, or click Save again to use it anyway.";
                _apiKeyConfirmedInvalid = true;
                return;
            }

            ApiKeyStatusText.Visibility = Visibility.Collapsed;
        }

        SaveApiKey(key);
    }

    /// <summary>Rewrites only .env's GEMINI_API_KEY line, preserving everything else the user may
    /// have hand-customized. A new key never takes effect until VoiceCtrl restarts:
    /// GeminiTranscriptionClient reads it once at startup, unlike Mode above, which is already
    /// live via TranscriptionModeStore.</summary>
    private void SaveApiKey(string key)
    {
        try
        {
            string[] lines = File.Exists(_envPath) ? File.ReadAllLines(_envPath) : [];
            File.WriteAllLines(_envPath, EnvFileEditor.ApplyUpdates(lines, new Dictionary<string, string> { ["GEMINI_API_KEY"] = key }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SimpleFileLogger.LogInfo($"Could not save .env: {ex.Message}");
            return;
        }

        _savedApiKey = key;
        _apiKeyConfirmedInvalid = false;
        ApiKeyStatusText.Visibility = Visibility.Collapsed;
        ApiKeyRestartBanner.Visibility = Visibility.Visible;
        RefreshApiKeySaveButtonState();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void DictationsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        DictationsSearchPlaceholder.Visibility = DictationsSearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshDictationsList();
    }

    private void RefreshDictationsList()
    {
        DictationsListPanel.Children.Clear();

        IReadOnlyList<DictationRecord> entries = _history.Search(DictationsSearchBox.Text);
        DictationsEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        string? lastGroupLabel = null;
        foreach (DictationRecord entry in entries)
        {
            string groupLabel = DayGroupLabel(entry.TimestampUtc);
            if (groupLabel != lastGroupLabel)
            {
                DictationsListPanel.Children.Add(BuildGroupLabel(groupLabel));
                lastGroupLabel = groupLabel;
            }

            DictationsListPanel.Children.Add(BuildDictationRow(entry));
        }
    }

    /// <summary>"Today"/"Yesterday" match the locked Quiet Ledger prototype; anything older falls
    /// back to a plain date rather than an ever-growing "N days ago" list.</summary>
    private static string DayGroupLabel(DateTime timestampUtc)
    {
        DateTime local = timestampUtc.ToLocalTime().Date;
        int daysAgo = (DateTime.Today - local).Days;
        return daysAgo switch
        {
            0 => "Today",
            1 => "Yesterday",
            _ => local.ToString("MMMM d"),
        };
    }

    private TextBlock BuildGroupLabel(string label) => new()
    {
        Text = label,
        Margin = new Thickness(30, 14, 30, 8),
        FontSize = 11,
        FontWeight = FontWeights.Bold,
        Foreground = (Brush)FindResource("HubInkFaintBrush"),
    };

    private Border BuildDictationRow(DictationRecord entry)
    {
        var timeText = new TextBlock
        {
            Text = entry.TimestampUtc.ToLocalTime().ToString("HH:mm"),
            FontFamily = (FontFamily)FindResource("PlexMonoFamily"),
            FontSize = 12.5,
            Foreground = (Brush)FindResource("HubInkFaintBrush"),
            Width = 46,
            Margin = new Thickness(0, 2, 18, 0),
        };

        var textBlock = new TextBlock
        {
            Text = entry.Text,
            FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("HubInkBrush"),
            Margin = new Thickness(0, 0, 18, 0),
        };

        var copyIcon = new FeatherIcon { Data = (Geometry)FindResource("Icon.Copy"), Size = 15 };
        var copyButton = new Button { Style = (Style)FindResource("HubIconButtonStyle"), Content = copyIcon, ToolTip = "Copy" };
        BindIconStroke(copyIcon, copyButton);
        copyButton.Click += (_, _) => CopyDictation(entry, copyIcon);

        var deleteIcon = new FeatherIcon { Data = (Geometry)FindResource("Icon.Trash2"), Size = 15 };
        var deleteButton = new Button
        {
            Style = (Style)FindResource("HubIconButtonDangerStyle"), Content = deleteIcon, ToolTip = "Delete",
            Margin = new Thickness(4, 0, 0, 0),
        };
        BindIconStroke(deleteIcon, deleteButton);
        deleteButton.Click += (_, _) => RequestDeleteDictation(entry);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(copyButton);
        actions.Children.Add(deleteButton);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(timeText, 0);
        Grid.SetColumn(textBlock, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(timeText);
        grid.Children.Add(textBlock);
        grid.Children.Add(actions);

        return new Border { Style = (Style)FindResource("DictationRowStyle"), Child = grid };
    }

    /// <summary>Mirrors the sidebar nav items' XAML binding (Stroke follows the button's
    /// Foreground) so hover/danger states recolor the icon along with everything else, without a
    /// second copy of each icon per state.</summary>
    private static void BindIconStroke(FeatherIcon icon, Button owner) =>
        icon.SetBinding(FeatherIcon.StrokeProperty, new Binding(nameof(Button.Foreground)) { Source = owner });

    private void CopyDictation(DictationRecord entry, FeatherIcon icon)
    {
        try
        {
            Clipboard.SetText(entry.Text, TextDataFormat.UnicodeText);
        }
        catch (COMException)
        {
            // Another process is holding the clipboard open; nothing to show for it here, unlike
            // the dictation pipeline's paste this isn't worth retrying for a manual Hub click.
            return;
        }

        icon.Data = (Geometry)FindResource("Icon.Check");
        var revertTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        revertTimer.Tick += (_, _) =>
        {
            icon.Data = (Geometry)FindResource("Icon.Copy");
            revertTimer.Stop();
        };
        revertTimer.Start();
    }

    private void LoadDictionaryFile()
    {
        string contents = File.Exists(UserDataPaths.Dictionary) ? File.ReadAllText(UserDataPaths.Dictionary) : string.Empty;
        _dictionaryHeader = CustomDictionary.ExtractHeader(contents);
        _dictionaryTerms.Clear();
        _dictionaryTerms.AddRange(CustomDictionary.Parse(contents.Split('\n')));
    }

    private void SaveDictionaryFile()
    {
        try
        {
            File.WriteAllText(UserDataPaths.Dictionary, CustomDictionary.Format(_dictionaryHeader, _dictionaryTerms));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Same best-effort stance as UserFileCache's own reads: the in-memory list (and the Hub's
            // display of it) keeps the edit either way, and the next successful save catches it up.
            SimpleFileLogger.LogInfo($"Could not save dictionary.txt: {ex.Message}");
        }
    }

    private void RefreshDictionaryList()
    {
        DictionaryListPanel.Children.Clear();

        if (_dictionaryAdding)
        {
            DictionaryListPanel.Children.Add(BuildDictionaryEditRow(string.Empty, CommitAddDictionaryTerm, CancelDictionaryEdit));
        }

        for (int index = 0; index < _dictionaryTerms.Count; index++)
        {
            string term = _dictionaryTerms[index];
            int capturedIndex = index;
            DictionaryListPanel.Children.Add(index == _dictionaryEditingIndex
                ? BuildDictionaryEditRow(term, text => CommitEditDictionaryTerm(capturedIndex, text), CancelDictionaryEdit)
                : BuildDictionaryRow(term, capturedIndex));
        }

        DictionaryEmptyText.Visibility = _dictionaryTerms.Count == 0 && !_dictionaryAdding ? Visibility.Visible : Visibility.Collapsed;
        DictionaryCountText.Text = $"{_dictionaryTerms.Count} / {CustomDictionary.MaxTerms}";
        AddDictionaryButton.IsEnabled = _dictionaryTerms.Count < CustomDictionary.MaxTerms;
    }

    private Border BuildDictionaryRow(string term, int index)
    {
        var textBlock = new TextBlock
        {
            Text = term,
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("HubInkBrush"),
        };

        var editIcon = new FeatherIcon { Data = (Geometry)FindResource("Icon.Edit2"), Size = 13 };
        var editButton = new Button { Style = (Style)FindResource("HubTextButtonStyle"), Content = BuildTextButtonContent(editIcon, "Edit") };
        BindIconStroke(editIcon, editButton);
        editButton.Click += (_, _) => StartEditDictionaryTerm(index);

        var removeIcon = new FeatherIcon { Data = (Geometry)FindResource("Icon.X"), Size = 13 };
        var removeButton = new Button
        {
            Style = (Style)FindResource("HubTextButtonDangerStyle"), Content = BuildTextButtonContent(removeIcon, "Remove"),
            Margin = new Thickness(2, 0, 0, 0),
        };
        BindIconStroke(removeIcon, removeButton);
        removeButton.Click += (_, _) => RequestRemoveDictionaryTerm(term);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(editButton);
        actions.Children.Add(removeButton);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(textBlock, 0);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(textBlock);
        grid.Children.Add(actions);

        return new Border { Style = (Style)FindResource("DictationRowStyle"), Child = grid };
    }

    private static StackPanel BuildTextButtonContent(FeatherIcon icon, string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(icon);
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(5, 0, 0, 0) });
        return panel;
    }

    /// <summary>Shared by both the "Add entry" row and an in-place term edit: a text box plus
    /// Save/Cancel icon buttons, replacing <see cref="BuildDictionaryRow"/> for whichever row is
    /// active. An accent border distinguishes it from the flat display rows around it.</summary>
    private Border BuildDictionaryEditRow(string initialText, Action<string> onCommit, Action onCancel)
    {
        var textBox = new TextBox
        {
            Text = initialText,
            MaxLength = CustomDictionary.MaxTermLength,
            FontFamily = (FontFamily)FindResource("PlexSansFamily"),
            FontSize = 13.5,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = (Brush)FindResource("HubBorderStrongBrush"),
            Padding = new Thickness(0, 0, 0, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = (Brush)FindResource("HubInkBrush"),
            Foreground = (Brush)FindResource("HubInkBrush"),
        };

        var confirmIcon = new FeatherIcon { Data = (Geometry)FindResource("Icon.Check"), Size = 15 };
        var confirmButton = new Button { Style = (Style)FindResource("HubIconButtonStyle"), Content = confirmIcon, ToolTip = "Save" };
        BindIconStroke(confirmIcon, confirmButton);
        confirmButton.Click += (_, _) => onCommit(textBox.Text);

        var cancelIcon = new FeatherIcon { Data = (Geometry)FindResource("Icon.X"), Size = 15 };
        var cancelButton = new Button
        {
            Style = (Style)FindResource("HubIconButtonStyle"), Content = cancelIcon, ToolTip = "Cancel",
            Margin = new Thickness(4, 0, 0, 0),
        };
        BindIconStroke(cancelIcon, cancelButton);
        cancelButton.Click += (_, _) => onCancel();

        textBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                onCommit(textBox.Text);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                onCancel();
                e.Handled = true;
            }
        };

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(confirmButton);
        actions.Children.Add(cancelButton);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(textBox, 0);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(textBox);
        grid.Children.Add(actions);

        var border = new Border
        {
            Margin = new Thickness(20, 0, 20, 6),
            Padding = new Thickness(16, 10, 16, 10),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("HubAccentBrush"),
            Background = (Brush)FindResource("HubPanelBrush"),
            Child = grid,
        };

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            textBox.Focus();
            textBox.SelectAll();
        });

        return border;
    }

    private void AddDictionaryButton_Click(object sender, RoutedEventArgs e)
    {
        _dictionaryEditingIndex = null;
        _dictionaryAdding = true;
        RefreshDictionaryList();
    }

    private void StartEditDictionaryTerm(int index)
    {
        _dictionaryAdding = false;
        _dictionaryEditingIndex = index;
        RefreshDictionaryList();
    }

    private void CancelDictionaryEdit()
    {
        _dictionaryAdding = false;
        _dictionaryEditingIndex = null;
        RefreshDictionaryList();
    }

    private void CommitAddDictionaryTerm(string rawText)
    {
        // Checked here too, not just via AddDictionaryButton.IsEnabled: that only stops the button
        // that opens this row, so without this a Save while already at the cap would grow the list
        // past CustomDictionary.MaxTerms, and CustomDictionary.Parse silently drops the overflow on
        // the very next load.
        if (_dictionaryTerms.Count >= CustomDictionary.MaxTerms)
        {
            return;
        }

        string? term = NormalizeDictionaryTerm(rawText, excludingIndex: null);
        if (term is null)
        {
            return; // Empty, too long, or a duplicate: leave the row open so the user can fix it.
        }

        _dictionaryTerms.Add(term);
        SaveDictionaryFile();
        _dictionaryAdding = false;
        RefreshDictionaryList();
    }

    private void CommitEditDictionaryTerm(int index, string rawText)
    {
        string? term = NormalizeDictionaryTerm(rawText, excludingIndex: index);
        if (term is null)
        {
            return;
        }

        _dictionaryTerms[index] = term;
        SaveDictionaryFile();
        _dictionaryEditingIndex = null;
        RefreshDictionaryList();
    }

    /// <summary>Trims and validates a candidate term, returning null for anything
    /// <see cref="CustomDictionary.Parse"/> would silently drop on the next load (blank, a comment,
    /// over <see cref="CustomDictionary.MaxTermLength"/>) or that collides case-insensitively with
    /// another entry, so the editor can never write a term that vanishes again by itself.</summary>
    private string? NormalizeDictionaryTerm(string rawText, int? excludingIndex)
    {
        string term = rawText.Trim();
        if (term.Length == 0 || term.Length > CustomDictionary.MaxTermLength || term.StartsWith('#'))
        {
            return null;
        }

        for (int i = 0; i < _dictionaryTerms.Count; i++)
        {
            if (i != excludingIndex && string.Equals(_dictionaryTerms[i], term, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return term;
    }

    private void RequestRemoveDictionaryTerm(string term)
    {
        ConfirmDialogTitleText.Text = $"Remove \"{term}\" from the Dictionary?";
        _pendingConfirmAction = () =>
        {
            _dictionaryTerms.RemoveAll(existing => string.Equals(existing, term, StringComparison.OrdinalIgnoreCase));
            SaveDictionaryFile();
            RefreshDictionaryList();
        };
        ConfirmDialogOverlay.Visibility = Visibility.Visible;
    }

    private void LoadSnippetsFile()
    {
        string contents = File.Exists(UserDataPaths.Snippets) ? File.ReadAllText(UserDataPaths.Snippets) : string.Empty;
        _snippetsHeader = SnippetTable.ExtractHeader(contents);
        _snippets.Clear();
        _snippets.AddRange(SnippetTable.Parse(contents.Split('\n')).Snippets);
    }

    private void SaveSnippetsFile()
    {
        try
        {
            File.WriteAllText(UserDataPaths.Snippets, SnippetTable.Format(_snippetsHeader, _snippets));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Same best-effort stance as UserFileCache's own reads: the in-memory list (and the Hub's
            // display of it) keeps the edit either way, and the next successful save catches it up.
            SimpleFileLogger.LogInfo($"Could not save snippets.txt: {ex.Message}");
        }
    }

    private void RefreshSnippetsList()
    {
        SnippetsListPanel.Children.Clear();

        if (_snippetAdding)
        {
            SnippetsListPanel.Children.Add(BuildSnippetEditRow(string.Empty, string.Empty, CommitAddSnippet, CancelSnippetEdit));
        }

        for (int index = 0; index < _snippets.Count; index++)
        {
            KeyValuePair<string, string> snippet = _snippets[index];
            int capturedIndex = index;
            SnippetsListPanel.Children.Add(index == _snippetEditingIndex
                ? BuildSnippetEditRow(snippet.Key, snippet.Value, (trigger, expansion) => CommitEditSnippet(capturedIndex, trigger, expansion), CancelSnippetEdit)
                : BuildSnippetRow(snippet, capturedIndex));
        }

        SnippetsEmptyText.Visibility = _snippets.Count == 0 && !_snippetAdding ? Visibility.Visible : Visibility.Collapsed;
        SnippetsCountText.Text = $"{_snippets.Count} / {SnippetTable.MaxSnippets}";
        AddSnippetButton.IsEnabled = _snippets.Count < SnippetTable.MaxSnippets;
    }

    private Border BuildSnippetRow(KeyValuePair<string, string> snippet, int index)
    {
        var triggerBlock = new TextBlock
        {
            Text = snippet.Key,
            FontFamily = (FontFamily)FindResource("PlexMonoFamily"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("HubInkBrush"),
            Width = 170,
            Margin = new Thickness(0, 2, 18, 0),
        };

        var expansionBlock = new TextBlock
        {
            Text = snippet.Value,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("HubInkDimBrush"),
            Margin = new Thickness(0, 0, 18, 0),
        };

        var editIcon = new FeatherIcon { Data = (Geometry)FindResource("Icon.Edit2"), Size = 13 };
        var editButton = new Button { Style = (Style)FindResource("HubTextButtonStyle"), Content = BuildTextButtonContent(editIcon, "Edit") };
        BindIconStroke(editIcon, editButton);
        editButton.Click += (_, _) => StartEditSnippet(index);

        var removeIcon = new FeatherIcon { Data = (Geometry)FindResource("Icon.X"), Size = 13 };
        var removeButton = new Button
        {
            Style = (Style)FindResource("HubTextButtonDangerStyle"), Content = BuildTextButtonContent(removeIcon, "Remove"),
            Margin = new Thickness(2, 0, 0, 0),
        };
        BindIconStroke(removeIcon, removeButton);
        removeButton.Click += (_, _) => RequestRemoveSnippet(snippet.Key);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(editButton);
        actions.Children.Add(removeButton);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(triggerBlock, 0);
        Grid.SetColumn(expansionBlock, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(triggerBlock);
        grid.Children.Add(expansionBlock);
        grid.Children.Add(actions);

        return new Border { Style = (Style)FindResource("DictationRowStyle"), Child = grid };
    }

    /// <summary>Shared by both the "Add snippet" row and an in-place snippet edit: a Trigger field
    /// and a multi-line Expansion field plus Save/Cancel icon buttons, replacing
    /// <see cref="BuildSnippetRow"/> for whichever row is active. Enter in the Trigger field moves
    /// to Expansion rather than committing, since Expansion needs its own Enter to insert a line
    /// break; Ctrl+Enter (from either field) or the Save button commits, Escape cancels.</summary>
    private Border BuildSnippetEditRow(string initialTrigger, string initialExpansion, Action<string, string> onCommit, Action onCancel)
    {
        var triggerLabel = new TextBlock
        {
            Text = "Trigger",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("HubInkFaintBrush"),
        };

        var triggerBox = new TextBox
        {
            Text = initialTrigger,
            FontFamily = (FontFamily)FindResource("PlexSansFamily"),
            FontSize = 13.5,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = (Brush)FindResource("HubBorderStrongBrush"),
            Padding = new Thickness(0, 4, 0, 4),
            Margin = new Thickness(0, 4, 0, 14),
            CaretBrush = (Brush)FindResource("HubInkBrush"),
            Foreground = (Brush)FindResource("HubInkBrush"),
        };

        var expansionLabel = new TextBlock
        {
            Text = "Expansion",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("HubInkFaintBrush"),
        };

        var expansionBox = new TextBox
        {
            Text = initialExpansion,
            FontFamily = (FontFamily)FindResource("PlexSansFamily"),
            FontSize = 13.5,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 56,
            MaxHeight = 140,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = (Brush)FindResource("HubBorderStrongBrush"),
            Padding = new Thickness(0, 4, 0, 4),
            Margin = new Thickness(0, 4, 0, 0),
            CaretBrush = (Brush)FindResource("HubInkBrush"),
            Foreground = (Brush)FindResource("HubInkBrush"),
        };

        var confirmIcon = new FeatherIcon { Data = (Geometry)FindResource("Icon.Check"), Size = 15 };
        var confirmButton = new Button { Style = (Style)FindResource("HubIconButtonStyle"), Content = confirmIcon, ToolTip = "Save" };
        BindIconStroke(confirmIcon, confirmButton);
        confirmButton.Click += (_, _) => onCommit(triggerBox.Text, expansionBox.Text);

        var cancelIcon = new FeatherIcon { Data = (Geometry)FindResource("Icon.X"), Size = 15 };
        var cancelButton = new Button
        {
            Style = (Style)FindResource("HubIconButtonStyle"), Content = cancelIcon, ToolTip = "Cancel",
            Margin = new Thickness(4, 0, 0, 0),
        };
        BindIconStroke(cancelIcon, cancelButton);
        cancelButton.Click += (_, _) => onCancel();

        triggerBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Keyboard.Focus(expansionBox);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                onCancel();
                e.Handled = true;
            }
        };

        expansionBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                onCommit(triggerBox.Text, expansionBox.Text);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                onCancel();
                e.Handled = true;
            }
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        actions.Children.Add(confirmButton);
        actions.Children.Add(cancelButton);

        var content = new StackPanel();
        content.Children.Add(triggerLabel);
        content.Children.Add(triggerBox);
        content.Children.Add(expansionLabel);
        content.Children.Add(expansionBox);
        content.Children.Add(actions);

        var border = new Border
        {
            Margin = new Thickness(20, 0, 20, 6),
            Padding = new Thickness(16, 14, 16, 14),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("HubAccentBrush"),
            Background = (Brush)FindResource("HubPanelBrush"),
            Child = content,
        };

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            triggerBox.Focus();
            triggerBox.SelectAll();
        });

        return border;
    }

    private void AddSnippetButton_Click(object sender, RoutedEventArgs e)
    {
        _snippetEditingIndex = null;
        _snippetAdding = true;
        RefreshSnippetsList();
    }

    private void StartEditSnippet(int index)
    {
        _snippetAdding = false;
        _snippetEditingIndex = index;
        RefreshSnippetsList();
    }

    private void CancelSnippetEdit()
    {
        _snippetAdding = false;
        _snippetEditingIndex = null;
        RefreshSnippetsList();
    }

    private void CommitAddSnippet(string rawTrigger, string rawExpansion)
    {
        // Checked here too, not just via AddSnippetButton.IsEnabled, for the same reason as the
        // Dictionary editor's cap check: a Save while already at the cap must not grow the list
        // past SnippetTable.MaxSnippets, since SnippetTable.Parse silently drops the overflow on
        // the very next load.
        if (_snippets.Count >= SnippetTable.MaxSnippets)
        {
            return;
        }

        string? trigger = NormalizeSnippetTrigger(rawTrigger, excludingIndex: null);
        if (trigger is null)
        {
            return; // Empty, contains '=', or a duplicate: leave the row open so the user can fix it.
        }

        _snippets.Add(new KeyValuePair<string, string>(trigger, rawExpansion.Trim()));
        SaveSnippetsFile();
        _snippetAdding = false;
        RefreshSnippetsList();
    }

    private void CommitEditSnippet(int index, string rawTrigger, string rawExpansion)
    {
        string? trigger = NormalizeSnippetTrigger(rawTrigger, excludingIndex: index);
        if (trigger is null)
        {
            return;
        }

        _snippets[index] = new KeyValuePair<string, string>(trigger, rawExpansion.Trim());
        SaveSnippetsFile();
        _snippetEditingIndex = null;
        RefreshSnippetsList();
    }

    /// <summary>Trims and validates a candidate trigger, returning null for anything that would be
    /// silently dropped by <see cref="SnippetTable.Parse"/> on the next load (blank), that would
    /// corrupt it (a literal '=' shifts where the trigger/expansion split falls when the file is
    /// re-read), or that collides case-insensitively with another trigger.</summary>
    private string? NormalizeSnippetTrigger(string rawText, int? excludingIndex)
    {
        string trigger = rawText.Trim();
        if (trigger.Length == 0 || trigger.Contains('='))
        {
            return null;
        }

        for (int i = 0; i < _snippets.Count; i++)
        {
            if (i != excludingIndex && string.Equals(_snippets[i].Key, trigger, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return trigger;
    }

    private void RequestRemoveSnippet(string trigger)
    {
        ConfirmDialogTitleText.Text = $"Remove \"{trigger}\" from Snippets?";
        _pendingConfirmAction = () =>
        {
            _snippets.RemoveAll(existing => string.Equals(existing.Key, trigger, StringComparison.OrdinalIgnoreCase));
            SaveSnippetsFile();
            RefreshSnippetsList();
        };
        ConfirmDialogOverlay.Visibility = Visibility.Visible;
    }

    private void LoadProfilesFile()
    {
        string contents = File.Exists(UserDataPaths.Profiles) ? File.ReadAllText(UserDataPaths.Profiles) : string.Empty;
        _profilesComment = AppProfileTable.ExtractComment(contents);
        _profiles.Clear();
        _profiles.AddRange(AppProfileTable.Parse(contents).Entries);
    }

    private void SaveProfilesFile()
    {
        try
        {
            File.WriteAllText(UserDataPaths.Profiles, AppProfileTable.Format(_profilesComment, _profiles));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Same best-effort stance as UserFileCache's own reads: the in-memory list (and the Hub's
            // display of it) keeps the edit either way, and the next successful save catches it up.
            SimpleFileLogger.LogInfo($"Could not save profiles.json: {ex.Message}");
        }
    }

    private void RefreshProfilesList()
    {
        ProfilesListPanel.Children.Clear();

        if (_profileAdding)
        {
            ProfilesListPanel.Children.Add(BuildProfileEditRow(string.Empty, new AppProfile(), CommitAddProfile, CancelProfileEdit));
        }

        for (int index = 0; index < _profiles.Count; index++)
        {
            KeyValuePair<string, AppProfile> entry = _profiles[index];
            int capturedIndex = index;
            ProfilesListPanel.Children.Add(index == _profileEditingIndex
                ? BuildProfileEditRow(entry.Key, entry.Value, (name, profile) => CommitEditProfile(capturedIndex, name, profile), CancelProfileEdit)
                : BuildProfileRow(entry, capturedIndex));
        }

        ProfilesEmptyText.Visibility = _profiles.Count == 0 && !_profileAdding ? Visibility.Visible : Visibility.Collapsed;
        ProfilesCountText.Text = _profiles.Count.ToString();
    }

    private Border BuildProfileRow(KeyValuePair<string, AppProfile> entry, int index)
    {
        var nameBlock = new TextBlock
        {
            Text = entry.Key,
            FontFamily = (FontFamily)FindResource("PlexMonoFamily"),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("HubInkBrush"),
        };

        var summaryBlock = new TextBlock
        {
            Text = DescribeProfile(entry.Value),
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("HubInkDimBrush"),
        };

        var textPanel = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        textPanel.Children.Add(nameBlock);
        textPanel.Children.Add(summaryBlock);

        var editIcon = new FeatherIcon { Data = (Geometry)FindResource("Icon.Edit2"), Size = 13 };
        var editButton = new Button { Style = (Style)FindResource("HubTextButtonStyle"), Content = BuildTextButtonContent(editIcon, "Edit") };
        BindIconStroke(editIcon, editButton);
        editButton.Click += (_, _) => StartEditProfile(index);

        var removeIcon = new FeatherIcon { Data = (Geometry)FindResource("Icon.X"), Size = 13 };
        var removeButton = new Button
        {
            Style = (Style)FindResource("HubTextButtonDangerStyle"), Content = BuildTextButtonContent(removeIcon, "Remove"),
            Margin = new Thickness(2, 0, 0, 0),
        };
        BindIconStroke(removeIcon, removeButton);
        removeButton.Click += (_, _) => RequestRemoveProfile(entry.Key);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(editButton);
        actions.Children.Add(removeButton);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(textPanel, 0);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(textPanel);
        grid.Children.Add(actions);

        return new Border { Style = (Style)FindResource("DictationRowStyle"), Child = grid };
    }

    /// <summary>A one-line summary of whichever fields are actually set, since most Profiles
    /// override only one or two of the four and a display row that always showed all four (mostly
    /// blank) would read worse than the fields themselves.</summary>
    private static string DescribeProfile(AppProfile profile)
    {
        var parts = new List<string>();
        if (profile.Tone is { Length: > 0 } tone)
        {
            parts.Add($"Tone: {tone}");
        }

        if (profile.Formatting is { Length: > 0 } formatting)
        {
            parts.Add($"Formatting: {formatting}");
        }

        if (profile.Cleanup is { Length: > 0 } cleanup)
        {
            parts.Add($"Cleanup: {cleanup}");
        }

        if (profile.Instructions is { Length: > 0 })
        {
            parts.Add("Instructions set");
        }

        return parts.Count == 0 ? "No overrides set" : string.Join("   ·   ", parts);
    }

    /// <summary>Shared by both the "Add profile" row and an in-place Profile edit: an application
    /// (process name) field, a free-text Tone field, fixed-choice Formatting/Cleanup Level dropdowns,
    /// and a multi-line Instructions field, plus Save/Cancel icon buttons, replacing
    /// <see cref="BuildProfileRow"/> for whichever row is active.</summary>
    private Border BuildProfileEditRow(string initialProcessName, AppProfile initialProfile, Action<string, AppProfile> onCommit, Action onCancel)
    {
        var processNameBox = BuildFieldTextBox(initialProcessName, mono: true);
        var toneBox = BuildFieldTextBox(initialProfile.Tone ?? string.Empty, mono: false);
        var formattingCombo = BuildOptionComboBox(FormattingOptions, initialProfile.Formatting);
        var cleanupCombo = BuildOptionComboBox(CleanupOptions, initialProfile.Cleanup);
        var instructionsBox = BuildFieldTextBox(initialProfile.Instructions ?? string.Empty, mono: false, multiline: true);

        var confirmIcon = new FeatherIcon { Data = (Geometry)FindResource("Icon.Check"), Size = 15 };
        var confirmButton = new Button { Style = (Style)FindResource("HubIconButtonStyle"), Content = confirmIcon, ToolTip = "Save" };
        BindIconStroke(confirmIcon, confirmButton);
        confirmButton.Click += (_, _) => onCommit(processNameBox.Text, new AppProfile
        {
            Tone = NullIfBlank(toneBox.Text),
            Formatting = FormattingOptions[formattingCombo.SelectedIndex].Value,
            Cleanup = CleanupOptions[cleanupCombo.SelectedIndex].Value,
            Instructions = NullIfBlank(instructionsBox.Text),
        });

        var cancelIcon = new FeatherIcon { Data = (Geometry)FindResource("Icon.X"), Size = 15 };
        var cancelButton = new Button
        {
            Style = (Style)FindResource("HubIconButtonStyle"), Content = cancelIcon, ToolTip = "Cancel",
            Margin = new Thickness(4, 0, 0, 0),
        };
        BindIconStroke(cancelIcon, cancelButton);
        cancelButton.Click += (_, _) => onCancel();

        void CancelOnEscape(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                onCancel();
                e.Handled = true;
            }
        }

        processNameBox.PreviewKeyDown += CancelOnEscape;
        toneBox.PreviewKeyDown += CancelOnEscape;
        instructionsBox.PreviewKeyDown += CancelOnEscape;

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        actions.Children.Add(confirmButton);
        actions.Children.Add(cancelButton);

        var content = new StackPanel();
        content.Children.Add(BuildFieldLabel("Application (process name)", isFirst: true));
        content.Children.Add(processNameBox);
        content.Children.Add(BuildFieldLabel("Tone"));
        content.Children.Add(toneBox);
        content.Children.Add(BuildFieldLabel("Formatting"));
        content.Children.Add(formattingCombo);
        content.Children.Add(BuildFieldLabel("Cleanup Level"));
        content.Children.Add(cleanupCombo);
        content.Children.Add(BuildFieldLabel("Instructions"));
        content.Children.Add(instructionsBox);
        content.Children.Add(actions);

        var border = new Border
        {
            Margin = new Thickness(20, 0, 20, 6),
            Padding = new Thickness(16, 14, 16, 14),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("HubAccentBrush"),
            Background = (Brush)FindResource("HubPanelBrush"),
            Child = content,
        };

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            processNameBox.Focus();
            processNameBox.SelectAll();
        });

        return border;
    }

    private TextBlock BuildFieldLabel(string text, bool isFirst = false) => new()
    {
        Text = text,
        Margin = isFirst ? new Thickness(0) : new Thickness(0, 12, 0, 0),
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = (Brush)FindResource("HubInkFaintBrush"),
    };

    private TextBox BuildFieldTextBox(string initialText, bool mono, bool multiline = false) => new()
    {
        Text = initialText,
        FontFamily = (FontFamily)FindResource(mono ? "PlexMonoFamily" : "PlexSansFamily"),
        FontSize = 13.5,
        AcceptsReturn = multiline,
        TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        MinHeight = multiline ? 56 : 0,
        MaxHeight = multiline ? 140 : double.PositiveInfinity,
        VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0, 0, 0, 1),
        BorderBrush = (Brush)FindResource("HubBorderStrongBrush"),
        Padding = new Thickness(0, 4, 0, 4),
        Margin = new Thickness(0, 4, 0, 0),
        CaretBrush = (Brush)FindResource("HubInkBrush"),
        Foreground = (Brush)FindResource("HubInkBrush"),
    };

    /// <summary>A Formatting/Cleanup Level dropdown restricted to the field's fixed value set (plus
    /// a "Default" option mapping to null), so the Profile Editor can never write a value that
    /// AppProfile's free-text Tone field is exempt from but these two are not.</summary>
    private ComboBox BuildOptionComboBox((string Label, string? Value)[] options, string? currentValue)
    {
        var comboBox = new ComboBox
        {
            FontFamily = (FontFamily)FindResource("PlexSansFamily"),
            FontSize = 13.5,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = (Brush)FindResource("HubBorderStrongBrush"),
            Padding = new Thickness(0, 4, 0, 4),
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = (Brush)FindResource("HubInkBrush"),
        };

        foreach ((string label, string? _) in options)
        {
            comboBox.Items.Add(label);
        }

        int selectedIndex = Array.FindIndex(options, option => string.Equals(option.Value, currentValue, StringComparison.OrdinalIgnoreCase));
        comboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;

        return comboBox;
    }

    private static string? NullIfBlank(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private void AddProfileButton_Click(object sender, RoutedEventArgs e)
    {
        _profileEditingIndex = null;
        _profileAdding = true;
        RefreshProfilesList();
    }

    private void StartEditProfile(int index)
    {
        _profileAdding = false;
        _profileEditingIndex = index;
        RefreshProfilesList();
    }

    private void CancelProfileEdit()
    {
        _profileAdding = false;
        _profileEditingIndex = null;
        RefreshProfilesList();
    }

    private void CommitAddProfile(string rawProcessName, AppProfile profile)
    {
        string? processName = NormalizeProfileProcessName(rawProcessName, excludingIndex: null);
        if (processName is null)
        {
            return; // Empty or a duplicate: leave the row open so the user can fix it.
        }

        _profiles.Add(new KeyValuePair<string, AppProfile>(processName, profile));
        SortProfiles();
        SaveProfilesFile();
        _profileAdding = false;
        RefreshProfilesList();
    }

    private void CommitEditProfile(int index, string rawProcessName, AppProfile profile)
    {
        string? processName = NormalizeProfileProcessName(rawProcessName, excludingIndex: index);
        if (processName is null)
        {
            return;
        }

        _profiles[index] = new KeyValuePair<string, AppProfile>(processName, profile);
        SortProfiles();
        SaveProfilesFile();
        _profileEditingIndex = null;
        RefreshProfilesList();
    }

    /// <summary>Keeps the in-memory list matching <see cref="AppProfileTable.Entries"/>'s documented
    /// alphabetical order immediately after an Add or a rename, rather than only once the Profiles
    /// tab is next reloaded — a Save that left a newly-added or renamed entry out of order made the
    /// very next Edit/Remove click land on the wrong row (caught by the Hub screenshot harness, not
    /// by any unit test, since the mismatch only shows up once a real click hits an on-screen row).</summary>
    private void SortProfiles() =>
        _profiles.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Trims and normalizes a candidate process name the same way <see cref="AppProfileTable.Parse"/>
    /// would on the next load (trim, strip a ".exe" suffix), returning null if it is empty or
    /// collides case-insensitively with another entry, so the editor can never write an entry that
    /// either vanishes or silently overwrites another one on the next load.</summary>
    private string? NormalizeProfileProcessName(string rawText, int? excludingIndex)
    {
        string processName = AppProfileTable.NormalizeProcessName(rawText);
        if (processName.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < _profiles.Count; i++)
        {
            if (i != excludingIndex && string.Equals(_profiles[i].Key, processName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return processName;
    }

    private void RequestRemoveProfile(string processName)
    {
        ConfirmDialogTitleText.Text = $"Remove the Profile for \"{processName}\"?";
        _pendingConfirmAction = () =>
        {
            _profiles.RemoveAll(existing => string.Equals(existing.Key, processName, StringComparison.OrdinalIgnoreCase));
            SaveProfilesFile();
            RefreshProfilesList();
        };
        ConfirmDialogOverlay.Visibility = Visibility.Visible;
    }

    private void RequestDeleteDictation(DictationRecord entry)
    {
        ConfirmDialogTitleText.Text = "Are you sure you want to delete this Dictation?";
        _pendingConfirmAction = () =>
        {
            _history.Remove(entry);
            RefreshDictationsList();
        };
        ConfirmDialogOverlay.Visibility = Visibility.Visible;
    }

    private void ConfirmDialogCancel_Click(object sender, RoutedEventArgs e)
    {
        _pendingConfirmAction = null;
        ConfirmDialogOverlay.Visibility = Visibility.Collapsed;
    }

    private void ConfirmDialogConfirm_Click(object sender, RoutedEventArgs e)
    {
        _pendingConfirmAction?.Invoke();
        _pendingConfirmAction = null;
        ConfirmDialogOverlay.Visibility = Visibility.Collapsed;
    }

    // ============ Settings > Hotkey tab (ticket 11) ============
    //
    // Built entirely in code into HotkeySubtabPanel, the same "clear and rebuild on every state
    // change" pattern as the Dictionary/Snippet/Profile lists above, since almost everything here
    // is conditional: an open key picker, Trigger Key on/off, single-key vs. chord mode, a live
    // reserved-key warning. HotkeySettings is saved immediately on every change (same immediacy as
    // SaveDictionaryFile et al.), and takes effect on the app's next restart (ticket 03's answer;
    // see the always-visible restart banner below).

    private void RefreshHotkeyPanel()
    {
        HotkeySubtabPanel.Children.Clear();

        string? triggerBareKey = GetTriggerBareKey();
        HotkeySubtabPanel.Children.Add(BuildSettingsFieldRow("Hotkey",
            "Double-tap to show or hide the Overlay. Always on. Single key only.",
            BuildHotkeyFieldControl(triggerBareKey)));

        HotkeySubtabPanel.Children.Add(BuildSettingsFieldRow("Trigger Key",
            "Optional hands-free key, off by default. Can't reuse the Hotkey's key.",
            BuildTriggerToggleControl()));

        if (_hotkeySettings.TriggerEnabled)
        {
            HotkeySubtabPanel.Children.Add(BuildSettingsFieldRow("Key",
                "Tap = start/stop, Hold = Hold-to-Talk.",
                BuildTriggerKeyDetail()));
        }

        HotkeySubtabPanel.Children.Add(BuildRestartBanner());
    }

    /// <summary>The bare key Trigger Key currently claims (its single key, or its chord's prefix),
    /// or null if Trigger Key is off. The Hotkey's own picker always excludes this, mirroring the
    /// locked prototype (.scratch/hub-ui-refresh/prototypes/14c-settings-chords.html): once Trigger
    /// Key has a concrete key, Hotkey can never be pointed at the same one.</summary>
    private string? GetTriggerBareKey()
    {
        if (!_hotkeySettings.TriggerEnabled)
        {
            return null;
        }

        return _hotkeySettings.TriggerMode == HotkeySettings.TriggerModeChord
            ? _hotkeySettings.ChordPrefix
            : _hotkeySettings.TriggerKey;
    }

    /// <summary>The Hotkey field's control: a badge + Change/Cancel button, or (while the chooser is
    /// open) the grouped key picker itself. Unlike Trigger Key's own picker below, Hotkey's warning
    /// only shows once it has actually been changed away from <see cref="HotkeyKeyCatalog.DefaultHotkeyKey"/>
    /// — the shipped default is never worth warning about.</summary>
    private UIElement BuildHotkeyFieldControl(string? triggerBareKey)
    {
        if (_hotkeyChooserOpen)
        {
            return BuildKeyPicker(_hotkeySettings.HotkeyKey, triggerBareKey, key =>
            {
                _hotkeySettings.HotkeyKey = key;
                _hotkeySettings.Save();
                _hotkeyChooserOpen = false;
                RefreshHotkeyPanel();
            });
        }

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        stack.Children.Add(BuildKeyBadgeRow(_hotkeySettings.HotkeyKey, chooserOpen: false, () =>
        {
            _hotkeyChooserOpen = true;
            RefreshHotkeyPanel();
        }));

        if (_hotkeySettings.HotkeyKey != HotkeyKeyCatalog.DefaultHotkeyKey
            && HotkeyKeyCatalog.WarningFor(_hotkeySettings.HotkeyKey) is string warning)
        {
            stack.Children.Add(BuildWarningBanner(warning));
        }

        return stack;
    }

    private ToggleButton BuildTriggerToggleControl()
    {
        var toggle = new ToggleButton
        {
            Name = "TriggerEnabledToggle",
            Style = (Style)FindResource("HubToggleStyle"),
            IsChecked = _hotkeySettings.TriggerEnabled,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        toggle.Click += (_, _) =>
        {
            _hotkeySettings.TriggerEnabled = toggle.IsChecked == true;
            if (!_hotkeySettings.TriggerEnabled)
            {
                // Mirrors the prototype: turning Trigger Key off clears its whole configuration
                // rather than leaving a stale key/chord behind for the next time it's turned on.
                _hotkeySettings.TriggerKey = null;
                _hotkeySettings.ChordPrefix = null;
                _hotkeySettings.ChordSecondKeyVk = null;
                _hotkeySettings.ChordSecondKeyLabel = null;
                _triggerChooserOpen = false;
                _chordListening = false;
            }

            _hotkeySettings.Save();
            RefreshHotkeyPanel();
        };
        return toggle;
    }

    private UIElement BuildTriggerKeyDetail()
    {
        var container = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };

        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 8) };
        modeRow.Children.Add(BuildModePillButton("Single key", _hotkeySettings.TriggerMode == HotkeySettings.TriggerModeSingle,
            () => SetTriggerMode(HotkeySettings.TriggerModeSingle)));
        modeRow.Children.Add(BuildModePillButton("Key combination", _hotkeySettings.TriggerMode == HotkeySettings.TriggerModeChord,
            () => SetTriggerMode(HotkeySettings.TriggerModeChord)));
        container.Children.Add(modeRow);

        container.Children.Add(_hotkeySettings.TriggerMode == HotkeySettings.TriggerModeChord
            ? BuildChordField()
            : BuildSingleTriggerField());

        return container;
    }

    private void SetTriggerMode(string mode)
    {
        _hotkeySettings.TriggerMode = mode;
        _triggerChooserOpen = false;
        _chordListening = false;
        _hotkeySettings.Save();
        RefreshHotkeyPanel();
    }

    private UIElement BuildSingleTriggerField()
    {
        // Only excluded once the Hotkey has actually been changed away from its default: the
        // default represents both Ctrl sides ambiguously, so it was never "claimed" from Trigger
        // Key's perspective either — same asymmetry as the locked prototype.
        string? hotkeyExclude = _hotkeySettings.HotkeyKey != HotkeyKeyCatalog.DefaultHotkeyKey ? _hotkeySettings.HotkeyKey : null;

        if (_hotkeySettings.TriggerKey is null || _triggerChooserOpen)
        {
            return BuildKeyPicker(_hotkeySettings.TriggerKey, hotkeyExclude, key =>
            {
                _hotkeySettings.TriggerKey = key;
                _hotkeySettings.Save();
                _triggerChooserOpen = false;
                RefreshHotkeyPanel();
            });
        }

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        stack.Children.Add(BuildKeyBadgeRow(_hotkeySettings.TriggerKey, chooserOpen: false, () =>
        {
            _triggerChooserOpen = true;
            RefreshHotkeyPanel();
        }));

        if (HotkeyKeyCatalog.WarningFor(_hotkeySettings.TriggerKey) is string warning)
        {
            stack.Children.Add(BuildWarningBanner(warning));
        }

        return stack;
    }

    private UIElement BuildChordField()
    {
        var container = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };

        container.Children.Add(BuildFieldCaption("Prefix"));

        var prefixWrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        string? hotkeyExclude = _hotkeySettings.HotkeyKey != HotkeyKeyCatalog.DefaultHotkeyKey ? _hotkeySettings.HotkeyKey : null;
        foreach (string prefix in HotkeyKeyCatalog.ChordPrefixes)
        {
            string capturedPrefix = prefix;
            prefixWrap.Children.Add(BuildKeyPill(prefix, isActive: prefix == _hotkeySettings.ChordPrefix, isExcluded: prefix == hotkeyExclude, () =>
            {
                _hotkeySettings.ChordPrefix = capturedPrefix;
                _hotkeySettings.Save();
                RefreshHotkeyPanel();
            }));
        }
        container.Children.Add(prefixWrap);

        container.Children.Add(BuildFieldCaption("+ second key", topMargin: 10));

        var captureButton = new Button
        {
            Style = (Style)FindResource("HubCaptureBoxStyle"),
            Content = _chordListening ? "Press a key..." : (_hotkeySettings.ChordSecondKeyLabel ?? "Click to capture"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Focusable = true,
        };
        if (_chordListening)
        {
            captureButton.BorderBrush = (Brush)FindResource("HubAccentBrush");
            captureButton.Foreground = (Brush)FindResource("HubAccentBrush");
        }
        captureButton.Click += (_, _) =>
        {
            _chordListening = true;
            RefreshHotkeyPanel();
        };
        captureButton.PreviewKeyDown += ChordCaptureButton_PreviewKeyDown;
        container.Children.Add(captureButton);

        if (_chordListening)
        {
            // Loaded priority, not Normal: the button this closure captured has to actually be in
            // the visual tree (this call is what puts it there) before Focus() can do anything.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => captureButton.Focus());
        }

        if (_hotkeySettings.ChordPrefix is not null && _hotkeySettings.ChordSecondKeyLabel is not null)
        {
            var resultRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            resultRow.Children.Add(BuildKeyBadge(_hotkeySettings.ChordPrefix));
            resultRow.Children.Add(new TextBlock { Text = "+", FontWeight = FontWeights.Bold, Margin = new Thickness(6, 0, 6, 0), Foreground = (Brush)FindResource("HubInkFaintBrush") });
            resultRow.Children.Add(BuildKeyBadge(_hotkeySettings.ChordSecondKeyLabel));
            container.Children.Add(resultRow);

            if (HotkeyKeyCatalog.ChordWarningFor(_hotkeySettings.ChordPrefix, _hotkeySettings.ChordSecondKeyLabel) is string warning)
            {
                container.Children.Add(BuildWarningBanner(warning));
            }
        }

        return container;
    }

    /// <summary>The chord's live second-key capture: a genuine keydown listener, not a list of
    /// buttons, since the second key is open-ended by design (letters, digits, Space, arrows,
    /// punctuation — anything at all). Only armed while <see cref="_chordListening"/>, so a key
    /// pressed anywhere else in the Hub while this button merely has focus is never mistaken for a
    /// capture.</summary>
    private void ChordCaptureButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_chordListening)
        {
            return;
        }

        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        _hotkeySettings.ChordSecondKeyVk = KeyInterop.VirtualKeyFromKey(key);
        _hotkeySettings.ChordSecondKeyLabel = FriendlyKeyName(key);
        _hotkeySettings.Save();
        _chordListening = false;
        RefreshHotkeyPanel();
    }

    /// <summary>A readable label for whatever key the chord's live capture just saw. Not
    /// exhaustive (OEM punctuation falls back to its raw enum name, e.g. "OemComma") — the same
    /// non-exhaustive stance as <see cref="HotkeyKeyCatalog.ChordWarnings"/>, since polishing every
    /// possible key's display name was never asked for, only that whatever is pressed is captured
    /// and shown.</summary>
    private static string FriendlyKeyName(Key key) => key switch
    {
        Key.Space => "Space",
        Key.Up => "Arrow Up",
        Key.Down => "Arrow Down",
        Key.Left => "Arrow Left",
        Key.Right => "Arrow Right",
        Key.Escape => "Esc",
        >= Key.D0 and <= Key.D9 => key.ToString()[1..],
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + key.ToString()[6..],
        _ => key.ToString(),
    };

    /// <summary>The grouped key picker (Modifiers / Function / Utility, per <see cref="HotkeyKeyCatalog.SingleKeyGroups"/>),
    /// shared by the Hotkey field and Trigger Key's single-key mode. <paramref name="excludeKey"/> is
    /// whatever the OTHER field currently claims, greyed out with a strikethrough and disabled
    /// rather than hidden, so it stays visible why that option is missing.</summary>
    private StackPanel BuildKeyPicker(string? current, string? excludeKey, Action<string> onPick)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };

        foreach (HotkeyKeyCatalog.KeyGroup group in HotkeyKeyCatalog.SingleKeyGroups)
        {
            panel.Children.Add(BuildFieldCaption(group.Label.ToUpperInvariant(), topMargin: 8));

            var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            foreach (string key in group.Keys)
            {
                string capturedKey = key;
                wrap.Children.Add(BuildKeyPill(key, isActive: key == current, isExcluded: key == excludeKey, () => onPick(capturedKey)));
            }
            panel.Children.Add(wrap);
        }

        if (excludeKey is not null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"{excludeKey} is unavailable here — already assigned.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 300,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = (Brush)FindResource("HubInkFaintBrush"),
            });
        }

        return panel;
    }

    private Button BuildKeyPill(string label, bool isActive, bool isExcluded, Action onClick)
    {
        var content = new TextBlock { Text = label };
        if (isExcluded)
        {
            content.TextDecorations = TextDecorations.Strikethrough;
        }

        var button = new Button
        {
            Style = (Style)FindResource("HubPillButtonStyle"),
            Content = content,
            IsEnabled = !isExcluded,
            ToolTip = isExcluded ? "In use by the other key" : null,
        };

        if (isActive)
        {
            button.Background = (Brush)FindResource("HubInkBrush");
            button.Foreground = Brushes.White;
        }

        button.Click += (_, _) => onClick();
        return button;
    }

    private Button BuildModePillButton(string label, bool isActive, Action onClick)
    {
        var button = new Button { Style = (Style)FindResource("HubPillButtonStyle"), Content = label };
        if (isActive)
        {
            button.Background = (Brush)FindResource("HubInkBrush");
            button.Foreground = Brushes.White;
        }

        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>The badge-plus-Change/Cancel row shared by the Hotkey field and Trigger Key's
    /// single-key mode once a key is actually set (the picker itself, <see cref="BuildKeyPicker"/>,
    /// covers the "not set yet, or currently changing it" case).</summary>
    private StackPanel BuildKeyBadgeRow(string keyLabel, bool chooserOpen, Action onToggleChooser)
    {
        var changeButton = new Button { Style = (Style)FindResource("HubTextButtonStyle"), Content = chooserOpen ? "Cancel" : "Change", Margin = new Thickness(8, 0, 0, 0) };
        changeButton.Click += (_, _) => onToggleChooser();

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(BuildKeyBadge(keyLabel));
        row.Children.Add(changeButton);
        return row;
    }

    private Border BuildKeyBadge(string text) => new()
    {
        Padding = new Thickness(10, 5, 10, 5),
        CornerRadius = new CornerRadius(6),
        BorderThickness = new Thickness(1),
        BorderBrush = (Brush)FindResource("HubBorderStrongBrush"),
        Background = (Brush)FindResource("HubBgBrush"),
        Child = new TextBlock
        {
            Text = text,
            FontFamily = (FontFamily)FindResource("PlexMonoFamily"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("HubInkBrush"),
        },
    };

    private TextBlock BuildFieldCaption(string text, double topMargin = 0) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Right,
        Margin = new Thickness(0, topMargin, 0, 4),
        Foreground = (Brush)FindResource("HubInkFaintBrush"),
    };

    private Border BuildWarningBanner(string text)
    {
        var icon = new FeatherIcon { Data = (Geometry)FindResource("Icon.AlertTriangle"), Size = 14 };
        icon.Stroke = (Brush)FindResource("HubWarningIconBrush");

        var content = new StackPanel { Orientation = Orientation.Horizontal, MaxWidth = 300 };
        content.Children.Add(icon);
        content.Children.Add(new TextBlock
        {
            Text = text + " Consider a different key.",
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = (Brush)FindResource("HubWarningInkBrush"),
        });

        return new Border
        {
            Background = (Brush)FindResource("HubWarningBgBrush"),
            BorderBrush = (Brush)FindResource("HubWarningBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = content,
        };
    }

    private Border BuildRestartBanner()
    {
        // DockPanel, not a horizontal StackPanel: a StackPanel offers its children infinite width
        // along the stack axis, so the TextBlock's TextWrapping would never actually kick in and
        // this sentence-plus-instructions-length message would just run off the panel unwrapped.
        var content = new DockPanel { LastChildFill = true };
        var icon = new FeatherIcon { Data = (Geometry)FindResource("Icon.RefreshCw"), Size = 14, Stroke = (Brush)FindResource("HubInkFaintBrush") };
        icon.SetValue(DockPanel.DockProperty, Dock.Left);
        icon.Margin = new Thickness(0, 1, 9, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        content.Children.Add(icon);
        content.Children.Add(new TextBlock
        {
            Text = "Hotkey / Trigger Key changes apply after VoiceCtrl restarts: right-click the tray icon, click Quit, then reopen VoiceCtrl.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("HubInkDimBrush"),
        });

        return new Border
        {
            Background = (Brush)FindResource("HubBgBrush"),
            BorderBrush = (Brush)FindResource("HubBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(30, 14, 30, 20),
            Child = content,
        };
    }

    /// <summary>Shared two-column field row (170px label+hint, right-aligned control), matching
    /// the Transcription tab's field layout exactly so the two subtabs read as one screen.</summary>
    private Border BuildSettingsFieldRow(string name, string hint, UIElement control)
    {
        // No explicit Width here: the 170px column below already constrains it, and adding a
        // second, equal Width on top of the right margin made the panel want 170+20=190px inside
        // a 170px slot, clipping the wrapped hint text instead of narrowing to fit it.
        var labelPanel = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
        labelPanel.Children.Add(new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("HubInkBrush") });
        labelPanel.Children.Add(new TextBlock
        {
            Text = hint,
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("HubInkFaintBrush"),
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(labelPanel, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(labelPanel);
        grid.Children.Add(control);

        return new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = (Brush)FindResource("HubBorderBrush"),
            Padding = new Thickness(30, 16, 30, 16),
            Child = grid,
        };
    }
}
