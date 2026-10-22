using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceCtrl.Core.Audio;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Dictation;
using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Overlay;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Setup;
using VoiceCtrl.Core.Status;
using VoiceCtrl.Core.Transcription;
using VoiceCtrl.Core.Tray;
using VoiceCtrl.Core.Updates;

namespace VoiceCtrl.Core.Bridge;

/// <summary>
/// System.Text.Json source generation for every bridge contract (spec §4): camelCase properties,
/// camelCase enum strings. A contract missing here fails <c>BridgeContractTests</c>, so adding a
/// record without listing it can't slip through to a runtime error.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [
        typeof(CamelCaseEnumConverter<Theme>),
        typeof(CamelCaseEnumConverter<ThemePreference>),
        typeof(CamelCaseEnumConverter<AppStatusKind>),
        typeof(CamelCaseEnumConverter<SetupListening>),
        typeof(CamelCaseEnumConverter<SetupKeyCheck>),
        typeof(CamelCaseEnumConverter<ModelDownloadState>),
        typeof(CamelCaseEnumConverter<KeyCaptureTarget>),
        typeof(CamelCaseEnumConverter<OverlayPhase>),
        typeof(CamelCaseEnumConverter<FailureReason>),
        typeof(CamelCaseEnumConverter<OverlayNotice>),
        typeof(CamelCaseEnumConverter<OverlayPointerKind>),
        typeof(CamelCaseEnumConverter<OverlayDock>),
        typeof(CamelCaseEnumConverter<KeepDictations>),
        typeof(CamelCaseEnumConverter<HubPasteOutcome>),
        typeof(CamelCaseEnumConverter<RetryOutcome>),
        typeof(CamelCaseEnumConverter<TranscriptionModePreference>),
        typeof(CamelCaseEnumConverter<OfflinePack>),
        typeof(CamelCaseEnumConverter<SmartCleanupTier>),
        typeof(CamelCaseEnumConverter<ApiKeyState>),
        typeof(CamelCaseEnumConverter<UpdateState>),
        typeof(CamelCaseEnumConverter<DictionaryEditError>),
        typeof(CamelCaseEnumConverter<SnippetEditError>),
        typeof(CamelCaseEnumConverter<ImportOutcome>),
        typeof(CamelCaseEnumConverter<ExportOutcome>),
        typeof(CamelCaseEnumConverter<ProfileEditError>),
        typeof(CamelCaseEnumConverter<Style>),
        typeof(CamelCaseEnumConverter<AppKind>),
        typeof(CamelCaseEnumConverter<TrayCorner>),
        typeof(CamelCaseEnumConverter<OnlineProvider>),
        typeof(CamelCaseEnumConverter<AudioInput>),
    ])]
[JsonSerializable(typeof(AppSnapshot))]
[JsonSerializable(typeof(AppReady))]
[JsonSerializable(typeof(SetTheme))]
[JsonSerializable(typeof(MinimizeWindow))]
[JsonSerializable(typeof(ToggleMaximizeWindow))]
[JsonSerializable(typeof(CloseWindow))]
[JsonSerializable(typeof(ThemeChanged))]
[JsonSerializable(typeof(ReducedMotionChanged))]
[JsonSerializable(typeof(AppStatus))]
[JsonSerializable(typeof(SetPaused))]
[JsonSerializable(typeof(StatusChanged))]
[JsonSerializable(typeof(WindowStateChanged))]
[JsonSerializable(typeof(SetupSnapshot))]
[JsonSerializable(typeof(GetSetupState))]
[JsonSerializable(typeof(KeyCheckResult))]
[JsonSerializable(typeof(CheckKey))]
[JsonSerializable(typeof(DownloadModel))]
[JsonSerializable(typeof(StopModelDownload))]
[JsonSerializable(typeof(FinishSetup))]
[JsonSerializable(typeof(ModelDownloadChanged))]
[JsonSerializable(typeof(ModelDownloadStatus))]
[JsonSerializable(typeof(DictationSettingsView))]
[JsonSerializable(typeof(GetDictationSettings))]
[JsonSerializable(typeof(DictationSettingsChanged))]
[JsonSerializable(typeof(SetMicrophone))]
[JsonSerializable(typeof(ClearRewriteKey))]
[JsonSerializable(typeof(ClearTriggerKey))]
[JsonSerializable(typeof(SetPressEnter))]
[JsonSerializable(typeof(SetMuteOtherAudio))]
[JsonSerializable(typeof(KeyCaptureOutcome))]
[JsonSerializable(typeof(CaptureKey))]
[JsonSerializable(typeof(CancelKeyCapture))]
[JsonSerializable(typeof(KeyCaptureHint))]
[JsonSerializable(typeof(MicrophoneDevice))]
[JsonSerializable(typeof(MicrophonesView))]
[JsonSerializable(typeof(GetMicrophones))]
[JsonSerializable(typeof(MicrophonesChanged))]
[JsonSerializable(typeof(SetMicPreview))]
[JsonSerializable(typeof(MicLevel))]
[JsonSerializable(typeof(OverlayState))]
[JsonSerializable(typeof(GetOverlayState))]
[JsonSerializable(typeof(OverlayViewChanged))]
[JsonSerializable(typeof(OverlayLevel))]
[JsonSerializable(typeof(OverlayPointer))]
[JsonSerializable(typeof(SetOverlayHitRect))]
[JsonSerializable(typeof(OverlayPainted))]
[JsonSerializable(typeof(StartFromOverlay))]
[JsonSerializable(typeof(CancelFromOverlay))]
[JsonSerializable(typeof(FinishFromOverlay))]
[JsonSerializable(typeof(PressFromOverlay))]
[JsonSerializable(typeof(RetryFromOverlay))]
[JsonSerializable(typeof(UndoFromOverlay))]
[JsonSerializable(typeof(DismissFromOverlay))]
[JsonSerializable(typeof(OpenKeySettings))]
[JsonSerializable(typeof(OverlayLayout))]
[JsonSerializable(typeof(GetOverlayLayout))]
[JsonSerializable(typeof(OverlayLayoutChanged))]
[JsonSerializable(typeof(OverlayDragChanged))]
[JsonSerializable(typeof(ShowHubScreen))]
[JsonSerializable(typeof(DictationItem))]
[JsonSerializable(typeof(FailedDictationItem))]
[JsonSerializable(typeof(DictationsView))]
[JsonSerializable(typeof(GetDictations))]
[JsonSerializable(typeof(DictationsChanged))]
[JsonSerializable(typeof(HubPasteResult))]
[JsonSerializable(typeof(PasteDictationAgain))]
[JsonSerializable(typeof(CopyDictation))]
[JsonSerializable(typeof(CopyAllResult))]
[JsonSerializable(typeof(CopyAllDictations))]
[JsonSerializable(typeof(RestoreDictationOriginal))]
[JsonSerializable(typeof(DeleteDictation))]
[JsonSerializable(typeof(UndoDeleteDictation))]
[JsonSerializable(typeof(ClearAllDictations))]
[JsonSerializable(typeof(RetryResult))]
[JsonSerializable(typeof(RetryDictation))]
[JsonSerializable(typeof(DismissFailedDictation))]
[JsonSerializable(typeof(OfflinePackInfo))]
[JsonSerializable(typeof(TranscriptionSettingsView))]
[JsonSerializable(typeof(GetTranscriptionSettings))]
[JsonSerializable(typeof(TranscriptionSettingsChanged))]
[JsonSerializable(typeof(SetMode))]
[JsonSerializable(typeof(SetDefaultCleanup))]
[JsonSerializable(typeof(SetProvider))]
[JsonSerializable(typeof(SaveOnlineKey))]
[JsonSerializable(typeof(RemoveOnlineKey))]
[JsonSerializable(typeof(SetOtherProvider))]
[JsonSerializable(typeof(OtherProviderSaved))]
[JsonSerializable(typeof(PickOfflinePack))]
[JsonSerializable(typeof(CancelOfflinePack))]
[JsonSerializable(typeof(SmartCleanupPackInfo))]
[JsonSerializable(typeof(SetSmartCleanup))]
[JsonSerializable(typeof(PickSmartCleanup))]
[JsonSerializable(typeof(CancelSmartCleanup))]
[JsonSerializable(typeof(RemoveSmartCleanup))]
[JsonSerializable(typeof(GetSuggestionSettings))]
[JsonSerializable(typeof(SuggestionSettingsView))]
[JsonSerializable(typeof(SuggestionSettingsChanged))]
[JsonSerializable(typeof(SetSuggestWords))]
[JsonSerializable(typeof(SaveTypeSafeKey))]
[JsonSerializable(typeof(RemoveTypeSafeKey))]
[JsonSerializable(typeof(UpdateView))]
[JsonSerializable(typeof(GeneralSettingsView))]
[JsonSerializable(typeof(GetGeneralSettings))]
[JsonSerializable(typeof(GeneralSettingsChanged))]
[JsonSerializable(typeof(SetSoundCues))]
[JsonSerializable(typeof(PlaySoundCues))]
[JsonSerializable(typeof(SetOverlayDock))]
[JsonSerializable(typeof(SetAlwaysShowOverlay))]
[JsonSerializable(typeof(KeepDictationsImpact))]
[JsonSerializable(typeof(PreviewKeepDictations))]
[JsonSerializable(typeof(SetKeepDictations))]
[JsonSerializable(typeof(SetEngineRecord))]
[JsonSerializable(typeof(OpenDataFolder))]
[JsonSerializable(typeof(CopyDiagnosticsResult))]
[JsonSerializable(typeof(CopyDiagnostics))]
[JsonSerializable(typeof(SetStartWithWindows))]
[JsonSerializable(typeof(CheckForUpdates))]
[JsonSerializable(typeof(DownloadUpdate))]
[JsonSerializable(typeof(SetAutomaticUpdates))]
[JsonSerializable(typeof(RestartToUpdate))]
[JsonSerializable(typeof(DismissUpdateCard))]
[JsonSerializable(typeof(ImportResult))]
[JsonSerializable(typeof(ExportResult))]
[JsonSerializable(typeof(DictionaryEntry))]
[JsonSerializable(typeof(DictionaryList))]
[JsonSerializable(typeof(ListDictionary))]
[JsonSerializable(typeof(DictionarySaveResult))]
[JsonSerializable(typeof(SaveDictionaryWord))]
[JsonSerializable(typeof(DictionaryRemoveResult))]
[JsonSerializable(typeof(RemoveDictionaryWord))]
[JsonSerializable(typeof(RestoreDictionaryWord))]
[JsonSerializable(typeof(ImportDictionary))]
[JsonSerializable(typeof(ExportDictionary))]
[JsonSerializable(typeof(Snippet))]
[JsonSerializable(typeof(SnippetList))]
[JsonSerializable(typeof(ListSnippets))]
[JsonSerializable(typeof(SnippetSaveResult))]
[JsonSerializable(typeof(SaveSnippet))]
[JsonSerializable(typeof(SnippetRemoveResult))]
[JsonSerializable(typeof(RemoveSnippet))]
[JsonSerializable(typeof(RestoreSnippet))]
[JsonSerializable(typeof(ImportSnippets))]
[JsonSerializable(typeof(ExportSnippets))]
[JsonSerializable(typeof(GetDictionarySuggestions))]
[JsonSerializable(typeof(SuggestionsView))]
[JsonSerializable(typeof(DictionarySuggestion))]
[JsonSerializable(typeof(AddDictionarySuggestion))]
[JsonSerializable(typeof(IgnoreDictionarySuggestion))]
[JsonSerializable(typeof(ProfileEntry))]
[JsonSerializable(typeof(ProfileList))]
[JsonSerializable(typeof(ListProfiles))]
[JsonSerializable(typeof(RunningApp))]
[JsonSerializable(typeof(RunningAppList))]
[JsonSerializable(typeof(ListRunningApps))]
[JsonSerializable(typeof(ProfileSaveResult))]
[JsonSerializable(typeof(SaveProfile))]
[JsonSerializable(typeof(RemoveProfile))]
[JsonSerializable(typeof(RestoreProfile))]
[JsonSerializable(typeof(StyleApp))]
[JsonSerializable(typeof(StyleExample))]
[JsonSerializable(typeof(StyleGroup))]
[JsonSerializable(typeof(StyleView))]
[JsonSerializable(typeof(GetStyle))]
[JsonSerializable(typeof(SetStyle))]
[JsonSerializable(typeof(SetStyleName))]
[JsonSerializable(typeof(MoveStyleApp))]
[JsonSerializable(typeof(TrayOpening))]
[JsonSerializable(typeof(PlaceTrayFlyout))]
[JsonSerializable(typeof(CloseTrayFlyout))]
[JsonSerializable(typeof(OpenHubFromTray))]
[JsonSerializable(typeof(OpenSettingsFromTray))]
[JsonSerializable(typeof(CopyLatestFromTray))]
[JsonSerializable(typeof(QuitFromTray))]
public sealed partial class BridgeJsonContext : JsonSerializerContext;

/// <summary>Enums travel as camelCase strings (<c>"light"</c>), never numbers, so the page's
/// string unions and the host agree and an unknown value fails loudly.</summary>
public sealed class CamelCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    where TEnum : struct, Enum;
