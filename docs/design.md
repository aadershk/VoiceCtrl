# VoiceCtrl: Windows Voice Dictation Utility

## Context

The goal was a dictation tool for Windows where a global hotkey summons a small floating bar anywhere in the OS. You toggle recording, speak, toggle again, and the cleaned-up transcription is injected at the text cursor in whatever application currently has focus: a terminal, a browser, an editor, Office, a chat app. The bar to clear was "as well integrated as Windows' own built-in voice dictation", so broad and reliable compatibility across arbitrary applications is the success criterion, not a proof of concept that works in one text box.

The comparable tools in this space are macOS-only, built on Swift, the Accessibility APIs and local-first Whisper transcription, with no Windows implementation to build on. This is a from-scratch design for Windows, and every platform mechanism below had to be worked out against Win32 rather than adapted from an existing project.

Key design decisions, and the reasoning behind each:

- **The hotkey is a double-tap of either bare Ctrl key, not Ctrl+Fn.** Fn is intercepted by keyboard firmware below the OS on most Windows laptops and is not reliably interceptable by any application, unlike on macOS where the OS does see it. Double-tapping a bare modifier is the closest equivalent, and it requires a low-level keyboard hook rather than `RegisterHotKey`, which cannot fire on a bare modifier with no companion key. Left and Right Ctrl are tracked independently, each chord-guarded so that a genuine Ctrl+letter shortcut, or two in a row such as copy then paste, never misreads as the hotkey. See "Hotkey hook" below.
- **C# and .NET 8 with WPF, not Python.** The entire value proposition rests on a global keyboard hook that must never silently drop, and on background stability across weeks of uptime. A compiled application carries materially lower risk on both counts than a Python build packaged with PyInstaller: interpreter GC pauses risk missing Windows' hook-callback timeout, and PyInstaller executables are prone to antivirus quarantine. The cost is a one-time .NET SDK install.
- **Gemini for online transcription, not local Whisper.** Gemini is a multimodal model rather than a pure speech-to-text engine, so transcription and filler-word and punctuation cleanup collapse into a single API call instead of two separate stages, which is simpler, faster and cheaper. A fully offline path on a local model was added afterwards; see "Mode switching" below.
- **Clipboard-paste injection, not raw keystroke simulation or a custom TSF IME.** Setting the clipboard and simulating Ctrl+V handles Unicode and multi-line text reliably and quickly across nearly every application. A full Text Services Framework IME, which is what Windows' own dictation uses underneath, would be the gold standard for compatibility but is a far larger engineering lift. It is noted as a possible future enhancement.

The Gemini wire format is isolated behind a single interface and file, so a change to the API surface is contained to one place. The request and response shapes documented below were confirmed against live documentation and a real call before any parsing code was written against them.

---

## Architecture Overview

```
[Either Ctrl double-tap] -> LowLevelKeyboardHook -> CtrlKeyTracker (chord-guard + per-key DoubleTapDetector)
    -> DictationStateMachine -> show OverlayWindow already recording (bottom-center, non-activating,
       target app keeps keyboard focus throughout)
    -> WasapiAudioRecorder captures at the device's native format, in memory, raising
       AudioLevelMeter readings at ~20Hz to drive the mic circle
[Either Ctrl double-tap again] -> recording stops -> resample to 16kHz/16-bit mono off the awaited
    path -> silence check ->
    AdaptiveTranscriptionClient routes to Gemini (online) or the local model (offline), per the
    Auto/Online/Offline mode set from the tray. Both paths see the user's dictionary and the
    active app's profile; snippets are expanded once, above the routing, so the result does not
    depend on which path served the audio -> cleaned text returned ->
    LastTranscriptionStore holds it -> ClipboardPasteInjector: save clipboard -> set clipboard to
    text -> SendInput Ctrl+V -> restore clipboard -> OverlayWindow auto-hides
[Esc while recording] -> recording stops, clip discarded, nothing transcribed, nothing pasted
```

Background shell: system tray icon (Pause/Resume, Mode [Auto/Online/Offline], Personalize [dictionary/snippets/profiles], Copy last transcription, Settings, Quit), autostart on Windows login. Runs non-elevated by default (least privilege; covers the overwhelming majority of real targets; elevated targets get a "text is on your clipboard, paste manually" fallback since UIPI blocks synthetic input into higher-integrity windows).

---

## Project Structure

```
C:\VoiceCtrl\
├── VoiceCtrl.sln
├── .gitignore
├── .env.example                                  (committed placeholder)
├── README.md
├── docs\design.md                                (this file)
├── src\
│   ├── VoiceCtrl\                            (WPF exe, net8.0-windows)
│   │   ├── app.manifest                          PerMonitorV2 DPI, asInvoker (NOT requireAdministrator)
│   │   ├── App.xaml / App.xaml.cs                composition root; no MainWindow; ShutdownMode=OnExplicitShutdown
│   │   ├── Overlay\OverlayWindow.xaml(.cs)        floating bar; WS_EX_NOACTIVATE/TOPMOST/TOOLWINDOW
│   │   ├── Overlay\MonitorPositioner.cs           bottom-center-of-active-monitor math
│   │   ├── Tray\TrayIconManager.cs                Hardcodet.NotifyIcon.Wpf setup + context menu
│   │   ├── Tray\AutoStartManager.cs               HKCU Run key register/unregister
│   │   ├── Tray\StartMenuShortcut.cs              self-healing "VoiceCtrl" .lnk via WScript.Shell COM, rewritten every launch
│   │   ├── Assets\app-icon.ico                    multi-res (16/32/48/64/256) icon, embedded via <ApplicationIcon>
│   │   └── Bootstrap\FirstRunSetup.cs             creates .env if missing, opens Notepad, registers autostart
│   │
│   └── VoiceCtrl.Core\                        (class library, net8.0-windows, UseWPF=true)
│       ├── Hotkey\LowLevelKeyboardHook.cs         SetWindowsHookEx wrapper + P/Invoke, Ctrl and Esc
│       ├── Hotkey\CtrlKeyTracker.cs               per-key chord-guard and repeat de-duplication
│       ├── Hotkey\TrackedCtrlKeyResolver.cs       raw hook event -> tracked VK, incl. Bluetooth fallback
│       ├── Hotkey\DoubleTapDetector.cs            pure logic, no Win32, unit testable
│       ├── Dictation\DictationStateMachine.cs     Idle/Recording/Processing; the toggle's only truth
│       ├── Audio\WasapiAudioRecorder.cs           NAudio WasapiCapture + resample + WaveFileWriter
│       ├── Audio\AudioLevelMeter.cs               RMS over the device's native format, throttled to ~20Hz
│       ├── Audio\Mp3Encoder.cs                    optional upload compression, off by default
│       ├── Audio\AudioClip.cs                     wavBytes + IsLikelySilent()
│       ├── Transcription\ITranscriptionClient.cs
│       ├── Transcription\GeminiTranscriptionClient.cs   HttpClient REST, sole file that knows the wire shape
│       ├── Transcription\GeminiModels.cs          request/response DTOs (defensive parsing)
│       ├── Transcription\GeminiFilesApiUploader.cs      resumable upload path for long clips (rarely used)
│       ├── Transcription\LocalTranscriptionClient.cs    offline ASR, no network call, lazy model load
│       ├── Transcription\OfflineTextPostProcessor.cs    the offline path's entire cleanup layer, all regex
│       ├── Transcription\FormattingHintMapper.cs        per-app bullet/prose directive, ToneHintMapper's sibling
│       ├── Transcription\ToneHintMapper.cs              per-app register hint, suppressible from .env
│       ├── Transcription\CleanupLevelMapper.cs          light/standard/aggressive -> prompt wording
│       ├── Transcription\LastTranscriptionStore.cs      in-memory only; backs the tray's Copy last transcription
│       ├── Transcription\AdaptiveTranscriptionClient.cs Auto/Online/Offline routing over the two clients above
│       ├── Personalization\PersonalizationStore.cs      owns the three user files, hands out parsed views
│       ├── Personalization\UserFileCache.cs             stat-based staleness check, reparse on change
│       ├── Personalization\CustomDictionary.cs          dictionary.txt parsing and its bounds
│       ├── Personalization\DictionaryCorrector.cs       offline-only bounded fuzzy correction
│       ├── Personalization\SnippetTable.cs              snippets.txt parsing + single-pass expansion
│       ├── Personalization\AppProfile.cs                one profiles.json entry
│       ├── Personalization\AppProfileTable.cs           profiles.json parsing, per-entry fault isolation
│       ├── Injection\ITextInjector.cs
│       ├── Injection\ClipboardPasteInjector.cs    save/set/SendInput Ctrl+V/restore, retry logic
│       ├── Injection\SendInputHelper.cs           P/Invoke SendInput wrapper
│       ├── Injection\ElevationChecker.cs          UIPI pre-check (compare token elevation)
│       ├── Config\AppConfig.cs / ConfigLoader.cs  hand-rolled .env parser, no extra dependency
│       ├── Config\TranscriptionModePreference.cs  Auto/Online/Offline enum
│       ├── Config\TranscriptionModeStore.cs       live mode preference; persists to %LocalAppData%\VoiceCtrl\prefs.json, not .env
│       ├── Config\UserDataPaths.cs                single source of truth for %LocalAppData%\VoiceCtrl and its files
│       ├── Interop\NativeMethods.cs               shared Win32: window styles, monitor, foreground window, token
│       └── Logging\SimpleFileLogger.cs
│
└── tests\VoiceCtrl.Core.Tests\                xunit: hotkey (DoubleTapDetector, CtrlKeyTracker, TrackedCtrlKeyResolver),
                                                    DictationStateMachine, ConfigLoader, silence detection, AudioLevelMeter,
                                                    FormattingHintMapper, ToneHintMapper, CleanupLevelMapper,
                                                    OfflineTextPostProcessor, the five Personalization types,
                                                    AdaptiveTranscriptionClient (injected fakes), TranscriptionModeStore
```

`VoiceCtrl.Core.csproj` needs `<UseWPF>true</UseWPF>` because it uses `System.Windows.Clipboard` for the injector; isolation here means one class per responsibility, not avoiding WPF assembly refs on a Windows-only project.

Publish (self-contained single-file, no separate .NET install needed on the end-user machine):
```
dotnet publish src\VoiceCtrl -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o publish
```
`PublishTrimmed` must stay **false**, since WPF doesn't reliably support IL trimming even on .NET 8.

---

## Key Implementation Details

### Hotkey hook (`Hotkey\LowLevelKeyboardHook.cs`, `Hotkey\CtrlKeyTracker.cs`, `Hotkey\TrackedCtrlKeyResolver.cs`, `Hotkey\DoubleTapDetector.cs`)

`SetWindowsHookEx(WH_KEYBOARD_LL, ...)`, installed on the **WPF UI/Dispatcher thread** in `App.OnStartup` (WH_KEYBOARD_LL requires a running Win32 message loop on the installing thread; WPF's Dispatcher already pumps one, so no second thread is needed). Tracks **both** `VK_LCONTROL` (0xA2) and `VK_RCONTROL` (0xA3), each with its own independent `DoubleTapDetector`, so alternating Left/Right never pairs, only two taps of the *same* side do.

`TrackedCtrlKeyResolver` maps a raw hook event to a tracked VK. Exact `VK_LCONTROL`/`VK_RCONTROL` match is authoritative. Some Bluetooth/wireless keyboards instead report the generic `VK_CONTROL` (0x11) at this low-level-hook layer regardless of which physical key was pressed. In that case, fall back to the `LLKHF_EXTENDED` flag (set = right, unset = left) to disambiguate. This is purely additive: it only engages when the exact match fails, so it cannot regress a keyboard that already reports the specific VK correctly.

Load-bearing gotchas:
1. **Keep the hook delegate alive in a field for the app's lifetime.** If it's only a local/lambda, GC can collect it while native code still holds the function pointer, which is the most common cause of "global hotkey stops working after a while."
2. **Callback must return in single-digit ms.** A callback that runs too long risks Windows silently unhooking it (`LowLevelHooksTimeout`), and since this hook shares the WPF UI thread, a slow callback also freezes the app AND the system-wide input pipeline. Only do: check `nCode >= 0`, skip `LLKHF_INJECTED` events, run the in-memory `CtrlKeyTracker`, and if it fires, `Dispatcher.BeginInvoke` (async, not `Invoke`) to show the overlay. Always `CallNextHookEx`.
3. **Filter `LLKHF_INJECTED` first, before any VK-specific logic.** Left Ctrl is a tracked key too, and it is the same VK that `SendInputHelper.SendCtrlV` synthesizes (see that file's comment), so this filter is the **sole** safeguard against the app's own synthetic paste self-triggering the hotkey. It must apply identically on the exact-match and Bluetooth-fallback resolution paths.
4. **Chord-guard, not just double-tap timing.** `CtrlKeyTracker` only counts a press as a "tap" if no other key went down while it was held, so Ctrl+C or Ctrl+V, including two of them back-to-back as in a fast copy-paste, never misreads as a hotkey press. A press is only provably clean once it releases without having chorded, so `DoubleTapDetected` fires on **key-up** (window math anchored at the stored key-down tick, not the release time), not key-down.
5. **De-duplicate OS key-repeat per tracked key.** `KBDLLHOOKSTRUCT` has no repeat flag (unlike classic `WM_KEYDOWN`). `CtrlKeyTracker` keeps an `IsDown` bool per key; only count a new logical press on the keydown-after-a-keyup, or holding the key will read as a rapid multi-tap.

`DoubleTapDetector` is pure logic (timestamps via `Environment.TickCount64`, not `DateTime.Now`, which avoids clock-adjustment hazards) with no Win32 dependency, so it's fully unit-testable in isolation. `CtrlKeyTracker` layers the per-key chord/repeat state machine on top and is unit-tested the same way. See `CtrlKeyTrackerTests.cs` for the Ctrl+C/Ctrl+V false-positive case specifically.

### Overlay window (`Overlay\OverlayWindow.xaml.cs`)

WPF: `WindowStyle="None"`, `AllowsTransparency="True"`, `ShowInTaskbar="False"`, `Topmost="True"`. `ShowActivated="False"` only suppresses activation on first `Show()`. It does **not** stop a later click from stealing focus, which is the actual risk (would rip focus from the target text field mid-dictation). Need the extended window style directly plus a `WM_MOUSEACTIVATE` handler:

```csharp
const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
// OnSourceInitialized: SetWindowLong(hwnd, GWL_EXSTYLE, current | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
// AddHook: on WM_MOUSEACTIVATE, set handled=true, return MA_NOACTIVATE.
```
(32-bit `GetWindowLong`/`SetWindowLong` is correct here since style bits fit in 32 bits even on x64. Don't "fix" this to the `LongPtr` variants, those are for pointer-sized values like `GWLP_WNDPROC`.)

**Interaction.** The double-tap toggles the dictation itself, not the bar's visibility: from hidden,
one double-tap shows the bar *and* starts recording in the same dispatch, so the bar never appears in
an idle state waiting to be clicked. The second double-tap stops and transcribes; Esc during
recording discards the clip without transcribing. `DictationStateMachine` (in Core, pure logic, unit
tested) is the only place that decides what a toggle means in a given state, and its Processing state
is what makes a second double-tap arriving mid-transcription a no-op. The mic click is kept as a
secondary path because it costs nothing and helps discovery.

**Level meter.** `AudioLevelMeter` computes RMS in `OnDataAvailable`, against the device's *native*
format rather than the resampled stream, since the resample only happens at stop. Readings are
throttled to ~20Hz and marshalled with `Dispatcher.BeginInvoke`, so a loud passage cannot flood the
UI thread. This is the whole of the "is it hearing me" feedback; live transcript preview was
considered and dropped, since it needs a second, streaming ASR model resident at all times.

**Multi-monitor positioning:** mark PerMonitorV2 DPI-aware in `app.manifest`; do bottom-center math in physical pixels via `GetForegroundWindow`, then `MonitorFromWindow(MONITOR_DEFAULTTONEAREST)`, then `GetMonitorInfo` (use `rcWork` so the bar doesn't overlap the taskbar), then convert to WPF DIPs using *that monitor's* DPI (`GetDpiForMonitor`), not the primary monitor's. Wrong on a single-monitor 100%-scale dev box only shows up as a misplaced bar on mixed-DPI multi-monitor setups, so it is worth a real test if a second display is available.

### Audio capture (`Audio\WasapiAudioRecorder.cs`)

`WasapiCapture` against the **Communications** role device (`enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)`), which matches how Windows itself picks a mic for VoIP/dictation-style use. WASAPI shared-mode hands back 32-bit float at the device's native rate (commonly 48kHz stereo, ~375KB/sec, which risks the Gemini 20MB inline cap past ~50s). Resample to **16-bit PCM mono 16kHz** via NAudio's `MediaFoundationResampler` (wraps Windows Media Foundation, no extra native dependency), giving ~32KB/sec and matching what Gemini downsamples to internally anyway.

Wrap the output `MemoryStream` in `NAudio.Utils.IgnoreDisposeStream` before handing it to `WaveFileWriter`, because disposing the writer patches the RIFF header (needed) but by default also closes the underlying stream (not needed, and breaks reading it back via `ToArray()`).

Subscribe to `RecordingStopped` (bridge to a `TaskCompletionSource`) rather than assuming `StopRecording()` synchronously drains the last buffers.

The resample is a single pass over the whole clip at stop, at `ResamplerQuality = 60`, and it runs
inside `Task.Run` rather than on the awaited path, so the UI thread is free while it happens. Doing
it once at the end rather than per-buffer during capture keeps `OnDataAvailable` cheap, which matters
because that callback also feeds the level meter.

`AudioClip.IsLikelySilent()`: duration-only check (under ~300ms), short-circuiting before ever calling Gemini for an accidental double-click with no real recording. A peak-amplitude check was tried first and measured against real hardware: a 2s room-tone sample peaked at 418/32767 while a real, cleanly-transcribed speech sample peaked at only 297/32767, so no fixed amplitude threshold classifies both correctly. Amplitude was dropped as a signal entirely. Gemini's `[NO_SPEECH_DETECTED]` sentinel is the sole content-based silence detector (see below).

### Transcription (`Transcription\GeminiTranscriptionClient.cs`)

Single combined transcribe and cleanup call. Default model: `gemini-3.7-flash` (audio-capable, released 2026-08-13, same tier as the earlier `gemini-3.6-flash` default but faster), kept in config (`GEMINI_MODEL_ID`) rather than hardcoded. A lower-latency, lower-quality opt-in (`gemini-3.5-flash-lite`) is documented in `.env.example` but is not the default, since it is a genuine quality-versus-speed tradeoff rather than a strict upgrade.

**Decision:** Google documents two ways to call Gemini: a newer **Interactions API** (`POST /v1beta/interactions`) and the classic **`generateContent`** endpoint (labeled "Legacy" but fully documented and functional). The classic endpoint is the right choice here:

- The Interactions API is a *stateful* resource model, where a request and its response are a stored "interaction" with a `steps` timeline, built for multi-turn tool-calling conversations. Its raw wire schema for extracting output text is not clearly documented outside SDK convenience accessors (`output_text`), which are unavailable here because this project uses plain `HttpClient` rather than an official SDK.
- `generateContent` has a flat, fully-documented, stable REST shape and remains explicitly supported.

Since this project only ever needs one-shot "send audio + prompt, get text back" with no multi-turn state, the Interactions API's statefulness is a cost with no matching benefit here. **`generateContent` is the implementation shape**, not a fallback:
```
POST https://generativelanguage.googleapis.com/v1beta/models/gemini-3.7-flash:generateContent
Headers: x-goog-api-key: <key>, Content-Type: application/json
Body: { "contents": [{ "role": "user", "parts": [
  { "text": "<prompt>" },
  { "inline_data": { "mime_type": "audio/wav", "data": "<base64>" } }
]}]}
Response: candidates[0].content.parts[0].text
```
Field casing (`inline_data`, `mime_type`) confirmed against live REST examples: snake_case, not the SDK's camelCase. 20MB inline cap (Files API above that, see below). `GeminiTranscriptionClient` is still the *only* file that knows the wire shape (behind `ITranscriptionClient`), so switching to the Interactions API later, if `generateContent` is ever actually sunset, stays a contained, single-file change.

Prompt (combined transcribe + cleanup in one pass) is a **template**, not a fixed constant
(`PromptTemplate` in `GeminiTranscriptionClient.cs`). Rule 2 is a `{0}` slot filled by
`CleanupLevelMapper.Resolve(CLEANUP_LEVEL)` (`light`/`standard`/`aggressive`; `standard` is worded
as the exact semantic union of the original hardcoded rules 2+3, so default behavior for anyone who
never sets `CLEANUP_LEVEL` is unchanged):
```
You are a dictation transcription engine. Listen to the attached audio clip and produce the
final cleaned-up text the speaker intended to write.

1. Transcribe accurately first, then silently clean up. Do not show the raw transcript or your work.
2. {0}
3. If the speaker corrects or retracts something mid-utterance (e.g. "send it to John...
   actually, no, make that Sarah", "the meeting is at 3, wait, 4 PM"), output only the
   final corrected version. Drop the retracted content entirely. Do not show both.
4. Preserve the speaker's actual meaning and wording. Do not summarize or add information.
5. Apply explicit formatting instructions ("new paragraph", "bullet point") instead of
   transcribing them literally. If the speaker enumerates three or more items with clear
   verbal markers ("first... second... third...", "one, two, three") without explicitly
   asking for a list, format them as one anyway.
6. Transcribe in the same language the speaker used.
7. If the audio is silent or has no discernible speech, respond with exactly: [NO_SPEECH_DETECTED]
8. Output ONLY the final text. No preamble, no quotes, no markdown fences, no "Here is...".
```
Rule 3 (mid-utterance correction/retraction handling) closes a gap identified by
comparing against Wispr-Flow-style competitors: the prior prompt only addressed filler words and
false starts (via `CleanupLevelMapper`), not full-clause retractions. Rule 5's implicit-list
detection was added at the same time. Both are best-effort interpretations, not verified against
the competitors' actual internal behavior. Real validation is real-world usage comparison.

`BuildPrompt` appends up to two further optional sections when configured: free-text
`DICTATION_STYLE_NOTES` (e.g. "never expand acronyms like API, UI, URL") and, when
`ENABLE_TONE_AWARENESS` is on, a per-app tone hint from `ToneHintMapper` keyed on the foreground
process name (`ForegroundAppDetector`, process name only, no window title, no OCR, deliberately
narrower than Wispr Flow's screenshot-based context awareness, which drew public privacy criticism).
Sampled at `TranscribeAsync`-call-time, not recording-start, so it stays consistent with where the
overlay's `WS_EX_NOACTIVATE` design keeps the foreground app if the user alt-tabs mid-recording.
All three sections are explicitly subordinate to rule 4 (preserve meaning) if they ever conflict with it.

A third optional section, under the same `ENABLE_TONE_AWARENESS` gate rather than a second toggle, appends a
formatting directive from `FormattingHintMapper`, `ToneHintMapper`'s sibling (own dictionary, own
`Resolve`; formatting and tone are independent axes, so kept separate rather than merged).
`windowsterminal`/`code`/`devenv`/`claude` get real Markdown bullets/numbered steps for enumerable
content; `slack`/`discord`/`whatsapp` stay continuous prose, no list syntax even with multiple
points; everything else is unchanged (today's rule 5 behavior). The `claude`/`whatsapp` process-name
guesses are correctable without a rebuild via optional `PROMPT_STYLE_APPS`/`CHAT_STYLE_APPS` `.env`
overrides (comma-separated, same pattern as `DICTATION_STYLE_NOTES`). Like the tone hint, this only
ever changes *structure*, never wording, and is hedged in-prompt ("ignore if it doesn't fit"). Offline
mode has no LLM in the loop (`LocalTranscriptionClient` is regex-based ASR cleanup, no
instruction-following), so this hint never applies there. A dictation that falls back to Offline
mid-flight (see Mode switching below) loses app-aware formatting for that one utterance.

`profiles.json` overrides all three axes per app (see Personalization below). An explicit `tone` in a
profile applies even when `ENABLE_TONE_AWARENESS=false`, because that flag governs VoiceCtrl
*guessing* a tone, not the user writing one down.

`generationConfig.thinkingConfig.thinkingLevel` (`THINKING_LEVEL`, default `low`) rides alongside
the prompt in the same request. `gemini-3.7-flash` defaults to `medium` if the field is omitted,
and only accepts `low`/`medium`/`high` for this model (`minimal` 400s with "not supported for this
model", confirmed live. Other models in the family accept `minimal`, which is why this is a
per-model default, not a universal one).
Confirmed live against Google's current REST reference: `generationConfig`/`thinkingConfig`/
`thinkingLevel` are genuinely camelCase, unlike the snake_case `inline_data`/`mime_type` above. A
real inconsistency in Gemini's REST surface, not a typo to "fix." Cannot combine with the legacy
`thinkingBudget` field (400 error), only `thinkingLevel`.

`[NO_SPEECH_DETECTED]` is the sole content-based silence detector. The local check is duration-only (see above), so this is what actually catches "recorded room tone with no real speech."

Files API (long clips): implement for completeness but expect it dormant in practice (a 3-minute dictation is ~5.6MB, well under the 20MB inline cap): resumable upload (`POST /upload/v1beta/files`, then the `x-goog-upload-url` header, then `POST` raw bytes with `X-Goog-Upload-Command: upload, finalize`), then reference the returned file URI instead of inline data. Branch on WAV byte size (~15MB threshold).

### Offline cleanup (`Transcription\OfflineTextPostProcessor.cs`)

Parakeet-TDT returns lowercase, largely unpunctuated text with every filler and repeat intact. This
file is the entire cleanup layer for that path, and it is deliberately all regex with no model in
the loop, so it costs microseconds and cannot hallucinate.

Order matters and is load-bearing:

1. Fillers and immediately repeated words are removed.
2. Spoken punctuation is substituted, if `SPOKEN_PUNCTUATION=true`.
3. Line-break commands are substituted.
4. `TidyPunctuation` repairs what steps 1-3 left behind. Removing "uh" from "so, uh, roll it out"
   leaves ", ," at that seam, and a break command consumed mid-sentence leaves a comma dangling at
   the start or end of a line. Doing this pass *after* the deletions rather than before is what
   makes the difference between output that reads as written and output that reads as edited.
5. Capitalization, first character directly and then sentence starts by regex.

Two defaults are chosen against the error asymmetry rather than against a hit rate:

- **Line breaks are always on** because "new line" and "new paragraph" are rare as ordinary English
  and unambiguous as commands. They are still guarded by a determiner lookbehind and an `of`
  lookahead, so "we are entering a new line of business" survives intact.
- **Spoken punctuation is off by default** because "comma" and "period" are ordinary words with no
  such guard available. Online mode still has the audio when it makes that call, so it needs no
  switch.

Sentence-start capitalization excludes the full stop that ends an abbreviation, which otherwise
turns "ship it, i.e. today" into "i.e. Today". Deliberately not implemented: self-correction
handling ("send it to John, actually no, Sarah") and spoken-number normalization. Both need context
a regex does not have and misfire on legitimate speech, so they stay Online-only capabilities and
are documented as offline limits in the README.

### Personalization (`Personalization\*`, three files in `%LocalAppData%\VoiceCtrl`)

Plain text and JSON, edited in Notepad from the tray, no settings dialog. `UserFileCache<T>` reparses
a file when its timestamp or length changes, checked by `FileInfo` stat rather than a
`FileSystemWatcher`: the check runs once per transcription against three small files, and it has
none of the watcher's failure modes (missed events on network paths, a handle held on a directory
the user may want to move, an event arriving while the editor has flushed half the file). A file
that cannot be read falls back to "no personalization" for that one dictation and logs, rather than
failing the dictation.

**`dictionary.txt`** is applied differently per path on purpose. Online, the terms go into the prompt
as a spelling reference, where the model can check a candidate against the audio it still has.
Offline, `DictionaryCorrector` runs over the finished text, which is all it has. Its thresholds are
timid by design: a miss costs the user one re-spoken word, a false correction silently rewrites a
word they did say. So the edit budget is zero below five characters, scales to three at thirteen,
a ~130-word common-English blocklist blocks fuzzy matches, and a case-only fix bypasses all of it
since rewriting the same letters carries no risk. Multi-word terms claim their span before
single-word terms get a look, which is what makes a file containing both "Schiphol" and "Amsterdam
Schiphol" behave the same whichever order they are written in.

**`snippets.txt`** is expanded once in `AdaptiveTranscriptionClient`, above the Online/Offline
routing, so a snippet behaves identically whichever path served the audio. That matters because in
Auto mode the user does not control which one did. Expansion is one left-to-right pass over an
alternation ordered longest-trigger-first, so a pair of snippets that name each other terminates
instead of looping.

**`profiles.json`** is an override layer, not a replacement. Resolution runs profile entry ->
`PROMPT_STYLE_APPS`/`CHAT_STYLE_APPS` from `.env` -> the built-in mappers. An absent field falls
through rather than blanking, so a user who edited one entry still picks up later improvements to
the built-ins, and only the literal `"none"` suppresses a hint. An unrecognized `formatting` value is
treated as a typo and falls through. Parsing goes through `Dictionary<string, JsonElement>` and
deserializes each entry inside its own try/catch, so a malformed entry costs that entry and not the
file. That is also what lets the seeded file carry a `_comment` array at the top, which a straight
`Dictionary<string, AppProfile>` deserialization would have thrown on, silently discarding
everything the user had written.

### Mode switching (`Config\TranscriptionModeStore.cs`, `Transcription\AdaptiveTranscriptionClient.cs`)

`TRANSCRIPTION_MODE` in `.env` used to be the one fixed choice read at startup. It's now only a
first-run seed: `TranscriptionModeStore` persists the live, tray-switchable Auto/Online/Offline
preference to `%LocalAppData%\VoiceCtrl\prefs.json`, deliberately not `.env`, which is never
programmatically rewritten (it holds the real API key). First run (no `prefs.json` yet) seeds
`Offline` only if `.env` explicitly set `TRANSCRIPTION_MODE=Offline` (pinning Offline is usually a
privacy choice, meaning never send audio to Google, so it is preserved rather than silently widened); everything
else seeds `Auto`, since `Auto` behaves identically to `Online` whenever there's internet and so only
ever adds a fallback. The seed is persisted immediately, so a later, unrelated `.env` edit can't
silently change an already-established preference. Once `prefs.json` exists it's the sole source of
truth; `.env`'s `TRANSCRIPTION_MODE` is never consulted again.

`AdaptiveTranscriptionClient` wraps a `GeminiTranscriptionClient` and a `LocalTranscriptionClient`
behind the same `ITranscriptionClient` interface `OverlayWindow` already talks to, choosing per call
from the live preference:
- `Offline`/`Online`: always that one client, no fallback; a real failure surfaces exactly as it
  always has (same `OverlayWindow` catch blocks, untouched).
- `Auto`: local client if no API key is configured, and silently so, since that is "not set up," not a failure;
  otherwise a fast `NetworkInterface.GetIsNetworkAvailable()` pre-check, then Gemini. An
  `HttpRequestException`/`TaskCanceledException` from that attempt (connectivity-shaped) falls back
  to local and raises `FellBackToOffline`, surfaced by `OverlayWindow` as a transient "Using offline
  mode" status; any other exception (a real API-level error, e.g. the `THINKING_LEVEL` 400 above)
  propagates unchanged rather than silently falling back, so a genuine bug can't hide behind "it
  still worked offline." A caller-driven cancellation is never mistaken for a connectivity problem
  either, since the fallback path rethrows instead of retrying offline when
  `cancellationToken.IsCancellationRequested`.

Prewarming (`PrewarmConnectionAsync`, called unconditionally on recording start, since the conditional
logic now lives inside this class, not at the call site) follows the same split, warming whichever
client(s) the current preference could actually need, both in parallel for `Auto`, since which
branch will be needed isn't known yet.

Tray UX: a "Mode" submenu (Auto/Online/Offline) next to Pause: three checkable, mutually-exclusive
`MenuItem`s. Selecting one updates `TranscriptionModeStore.Current` and calls `Save()` immediately;
same "mutable property the caller reads synchronously" shape `IsPaused` already used, not a new
eventing mechanism.

### Text injection (`Injection\ClipboardPasteInjector.cs`, `SendInputHelper.cs`, `ElevationChecker.cs`)

Use WPF's `System.Windows.Clipboard`/`IDataObject`, not raw P/Invoke per-format calls. Set only `TextDataFormat.UnicodeText` on the outgoing paste (deliberate, since it inserts as plain text inheriting the target's own formatting rather than carrying formatting in).

```csharp
var saved = SnapshotClipboard(); // eagerly materializes every format into a plain DataObject, see gotcha below
Clipboard.SetText(transcribedText, TextDataFormat.UnicodeText);
// SendInput: LCONTROL down, V down, V up, LCONTROL up (4-element INPUT[] array)
// short delay, then:
if (saved != null) Clipboard.SetDataObject(saved, copy: true);
```

Gotchas:
- STA thread required (WPF UI thread is STA by default, so preserve `[STAThread]` if `Main` is ever customized).
- **`Clipboard.GetDataObject()` returns a lazy proxy, not a snapshot. Never hand its result straight to a later `SetDataObject` restore.** Measured directly against real targets: the naive `var saved = Clipboard.GetDataObject(); ... Clipboard.SetDataObject(saved, copy: true);` pattern does *not* throw at restore time, but a subsequent read of the "restored" clipboard throws `COMException 0x800401D3 CLIPBRD_E_BAD_DATA`. Root cause: the returned `IDataObject`'s `GetData()` reaches through to the *live* OLE clipboard at call time rather than copying bytes up front, so once `Clipboard.SetText(transcribedText, ...)` overwrites the clipboard in between, that proxy is reading through to already-stale content by the time the `copy: true` restore forces a flush. Reproduced identically against both real Notepad and a bare in-process WPF `TextBox` target, which rules out Notepad-specific and harness-specific causes. It only went away after rewriting the snapshot step to eagerly walk `live.GetFormats()` and `SetData(format, live.GetData(format), autoConvert: false)` into a fresh `new DataObject()` at snapshot time, while the original clipboard owner is still alive.
- **Never `ConfigureAwait(false)` in the chain running from stop-recording through the Gemini call to injection.** The WPF `SynchronizationContext` is what brings execution back to the STA UI thread after each `await`, which is what makes calling `Clipboard`/`SendInput` safe right after awaiting the HTTP response.
- Transient `COMException` (`CLIPBRD_E_CANT_OPEN`) is normal if another process holds the clipboard open, so wrap set/restore in a 2-3 attempt retry, ~75ms apart.
- `SendInput`'s `cbSize` must be `Marshal.SizeOf<INPUT>()` computed at runtime, and `MOUSEINPUT`/`HARDWAREINPUT` must be fully defined even though unused (an undersized union is a common copy-paste bug that makes `SendInput` silently fail on some machines).

**UIPI / elevation:** pre-check *before* attempting `SendInput`, which gives no reliable synchronous failure signal. The chain is `GetForegroundWindow`, `GetWindowThreadProcessId`, `OpenProcess`, `OpenProcessToken`, `GetTokenInformation(TokenElevation)`, comparing the target process against this one. If the target is elevated and this process is not: skip the SendInput attempt, still set the clipboard, show the manual-paste message "Copied. Press Ctrl+V (elevated window)" (manual real-hardware Ctrl+V is not blocked by UIPI, only synthetic input is), and skip the clipboard auto-restore so the text is still there when the user pastes manually.

**Injection never silently loses text.** `InjectAsync` returns `Injected`, `ClipboardOnlyElevatedTarget`
or `Failed`, and the whole body is wrapped against `COMException`, so a clipboard the app genuinely
could not open reports `Failed` rather than escaping as an unhandled exception on the UI thread. On
`Failed` the bar says "Paste failed. Tray: Copy last transcription", and the text is in
`LastTranscriptionStore` regardless of which of the three outcomes happened, because it is recorded
before the injection is attempted.

**Default: run non-elevated.** Least privilege for a tool that touches mic/clipboard/keystroke-injection system-wide; `HKCU\...\Run` autostart can't silently launch elevated without a UAC prompt at every login; the large majority of real targets (browsers, chat, IDEs, terminals, Office) are non-elevated anyway. The rare elevated target (admin PowerShell, Task Manager) gets the manual-paste fallback above.

### Error handling

| Scenario | Detection | Behavior |
|---|---|---|
| No mic permission | `WasapiCapture.StartRecording()` throws (`E_ACCESSDENIED` or no device) | Distinct bar message ("Microphone access is blocked" / "No microphone found"), auto-hide, never crash |
| No internet / API error / timeout | `HttpRequestException`, `TaskCanceledException` (~30s CTS timeout), or non-2xx with Gemini's error body | Short human-readable bar message; **never touch the clipboard on this path**, only set it after a successful response, right before injection |
| Empty/silent recording | Local `IsLikelySilent()` duration pre-check (accidental double-click only), or `[NO_SPEECH_DETECTED]` sentinel (actual silence/room tone) | Skip the API call on the local check; otherwise "No speech detected" once Gemini returns the sentinel; either way no injection, no clipboard touch |
| Clipboard restore fails | `SetDataObject` throws after retries, during *restore* (not *set*) | Paste already succeeded, so never block on this. Retry 2-3x; if still failing, low-severity tray balloon, not an in-bar error |
| Paste into elevated window | `ElevationChecker` pre-check | See UIPI section above: clipboard set, manual-paste message, skip auto-restore |

### Tray shell & config

`TrayIconManager` (Hardcodet.NotifyIcon.Wpf `TaskbarIcon`: Pause/Resume, Mode, Personalize, Copy last transcription, Settings which opens `.env` in Notepad, and Quit), `AutoStartManager` (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`), `FirstRunSetup` (create `.env` from `.env.example` if missing, open in Notepad, register autostart, one-time tray balloon).

The menu takes its editable files as a `TrayFileEntry` list rather than knowing the three
personalization paths itself, and raises `OpenFileRequested`, so adding a fourth file later is a
change to the composition root and nothing else. **Copy last transcription** reads
`LastTranscriptionStore`, which is in-memory only and deliberately not persisted: it exists so a
failed paste is never a lost dictation, not to build a history of everything the user has ever
dictated. Its menu item is enabled or disabled in the menu's `Opened` handler, so it reflects the
state at the moment the user looks at it.

**Single-file-publish gotcha:** locate the exe via `AppContext.BaseDirectory`/`Environment.ProcessPath`, never `Assembly.GetExecutingAssembly().Location`, which returns empty or misleading under `PublishSingleFile=true`. Breaks `.env` lookup and autostart path registration silently, only in the published build.

Config loaded via a hand-rolled ~20-line parser (no `DotNetEnv`/`Microsoft.Extensions.Configuration` dependency, consistent with avoiding an unofficial Gemini SDK too).

| `.env` key | Default | Purpose |
|---|---|---|
| `GEMINI_API_KEY` | *(empty)* | Required for transcription; app runs with mic/hotkey/overlay but shows "Add your Gemini API key" until set |
| `GEMINI_MODEL_ID` | `gemini-3.7-flash` | Model passed to `generateContent`; free-text, no client-side validation, so a bad value 400s via the existing error path |
| `DOUBLE_TAP_WINDOW_MS` | `400` | Max gap between Ctrl taps that still counts as a double-tap |
| `AUTO_HIDE_DELAY_MS` | `1500` | How long an error/status message stays in the bar before auto-hiding |
| `THINKING_LEVEL` | `low` | `generationConfig.thinkingConfig.thinkingLevel` sent with every request; lower = faster, less reasoning. `gemini-3.7-flash` rejects `minimal` (400), so `low` is the fastest level it accepts |
| `DICTATION_STYLE_NOTES` | *(empty)* | Free-text preferences appended to the prompt verbatim, e.g. personal wording/acronym rules |
| `ENABLE_TONE_AWARENESS` | `true` | Whether to append a per-foreground-app tone hint (see prompt section above) |
| `CLEANUP_LEVEL` | `standard` | `light`/`standard`/`aggressive`, controlling how much the transcript is rephrased/restructured beyond filler removal |
| `PROMPT_STYLE_APPS` | *(empty)* | Additive override merged with the built-in structured-formatting app list (see formatting hint above); wrong guesses like `claude` are fixable here without a rebuild |
| `CHAT_STYLE_APPS` | *(empty)* | Additive override merged with the built-in prose-formatting app list; same purpose as `PROMPT_STYLE_APPS` for the opposite bucket |
| `TRANSCRIPTION_MODE` | `Online` | First-run seed only for the tray-switchable Auto/Online/Offline preference (see Mode switching below); ignored once `%LocalAppData%\VoiceCtrl\prefs.json` exists |
| `LOCAL_MODEL_VARIANT` | `parakeet-tdt-0.6b-v2` | Subfolder of `%LocalAppData%\VoiceCtrl\models` the offline model is loaded from |
| `LOCAL_NUM_THREADS` | `Clamp(ProcessorCount / 2, 2, 8)` | ONNX intra-op threads for the offline model. Measured on a 16-core machine: 2 threads 6,590ms on the long clip, 6 threads 3,508ms, 8 threads 3,449ms. Gains flatten past six, and every extra thread competes with whatever the user is actually working in, hence the clamp |
| `COMPRESS_UPLOAD` | `false` | MP3-encode the clip before uploading in Online mode. Cuts the payload to ~13% of the WAV, measured. Off by default because the effect on transcription accuracy has not been measured against the live API |
| `SPOKEN_PUNCTUATION` | `false` | Offline only: substitute spoken "comma"/"period"/"question mark". Off by default, see Offline cleanup above |

Every key has a safe code-level default, so an existing `.env` from before any given pass keeps
working untouched; only someone who wants to change a default needs to add a line by hand.
Like every other `.env` setting, changes only take effect after a restart (no hot-reload). The three
personalization files are the exception, and are re-read on the next dictation after a save.

---

## NuGet Dependencies

| Package | Version | Note |
|---|---|---|
| `NAudio` | **`2.3.0`** exact pin | NAudio 3.0.0 (latest) requires net9.0+, forced by a .NET 8 GC bug. A bare unpinned install would silently resolve to 3.0.0 and fail to restore against net8.0-windows. 2.x remains the actively-maintained line for net8 and older TFMs. Clean restore, 0 warnings. |
| `Hardcodet.NotifyIcon.Wpf` | `2.0.1` | **Swapped from `H.NotifyIcon.Wpf 2.4.1`.** That package's 2.4.1 nuspec ships only `.NETFramework4.6.2` and `net10.0-windows7.0` assets, with no net8 target at all, so it restored against this project's net8.0-windows TFM through NuGet's `.NETFramework` compatibility shim and raised NU1701. `Hardcodet.NotifyIcon.Wpf 2.0.1` natively targets `net8.0-windows7.0` and restores with 0 warnings. Note that the namespace is `Hardcodet.Wpf.TaskbarNotification.TaskbarIcon`, not `H.NotifyIcon`. |

No JSON/config package needed, since `System.Text.Json` ships in the .NET 8 shared framework.

---

## Verification

Manual test matrix:

| Target | Pass condition |
|---|---|
| Notepad | Text lands exactly at cursor; rest of document undisturbed; clipboard restored |
| Windows Terminal, PowerShell | Pastes as literal input; **no trailing newline** from our code (would risk auto-executing dictated text as a command); multi-line paste confirmation dialog appearing is expected Terminal behavior, not a bug |
| Windows Terminal, WSL/bash | Same, plus confirm smart quotes/em-dashes from cleanup survive intact |
| VS Code editor | Multi-line dictation inserts as real newlines; `Ctrl+Z` undoes cleanly as a normal paste |
| Chrome address bar | No stray whitespace; omnibox autocomplete unaffected |
| Chrome textarea / Gmail compose | Plain text inheriting surrounding formatting (consequence of setting only `CF_UNICODETEXT`) |
| Word/WordPad | Inserted at cursor, inherits paragraph formatting, no autocorrect artifacts |
| Slack/Discord (if installed) | Multi-line dictation pastes within one message, doesn't auto-send (this works specifically because injection is one clipboard paste, not per-character key simulation) |

Hotkey false-trigger checks:
1. ~60s of normal prose typing + Ctrl shortcuts (`Ctrl+S`, `Ctrl+B`, etc., both Left and Right) in Notepad. The bar must never appear.
2. Two taps of the same Ctrl key (either side) <400ms apart: triggers.
3. Two taps ~600ms+ apart: does not trigger.
4. Hold either Ctrl key 2+ seconds: does not trigger (tests key-repeat de-dupe).
5. Rapid-fire 5+ taps of the same side in under a second: triggers exactly once per qualifying pair, no misfire.
6. One Left-Ctrl tap immediately followed by one Right-Ctrl tap (or vice versa): does not trigger; the two sides are tracked independently.
7. Rapid `Ctrl+C` then `Ctrl+V` (real copy-paste, <400ms apart) in Notepad: the bar must never appear. This is the chord-guard's reason for existing: without it, this exact gesture would misread as a qualifying double-tap.

Latency & prompt-customization checks (requires a live `GEMINI_API_KEY`, cannot be automated,
needs a human ear on the output; this pass folds in and supersedes the matrix above, since
it now has real new cases to cover):
1. A dictation with real "um"/"uh" filler still gets it stripped at `THINKING_LEVEL=low` (the default; `minimal` 400s on `gemini-3.7-flash`): confirms lower thinking doesn't skip cleanup.
2. A clip with an explicit spoken formatting instruction ("new paragraph", "bullet point") still gets applied, not transcribed literally. This is the rule needing the most reasoning, most likely to degrade first under a lower thinking budget.
3. A silent/room-tone clip still reliably returns `[NO_SPEECH_DETECTED]`.
4. Compare latency and output quality against the `medium` baseline (temporarily unset `THINKING_LEVEL`) on the same clip.
5. Re-run 1-4 with `DICTATION_STYLE_NOTES` and a recognized foreground app (e.g. Slack, VS Code) active, and `CLEANUP_LEVEL=aggressive`. A longer prompt with more instructions pulls against "less thinking," so the interaction needs verifying together, not just each piece alone.
6. Check `%LOCALAPPDATA%\VoiceCtrl\log.txt` after a few dictations for `Pipeline timing: stop=…ms transcribe=…ms inject=…ms total=…ms` lines, and confirm pre-warming isn't silently erroring (no unexpected `PrewarmConnectionAsync`-related entries).

Formatting & mode-switching checks (the connectivity-fallback branch itself is covered by
`AdaptiveTranscriptionClientTests`' injected fakes, not a live network toggle):
1. Synthesize a clip with a clear enumerable structure (e.g. "I need three things: first, buy milk, second, call mom, third, finish the report") and transcribe it once with the foreground app forced to `windowsterminal` and once to `whatsapp`, then confirm the two outputs actually differ in list structure per the `FormattingHintMapper` bucket table, not just in wording.
2. With a live `GEMINI_API_KEY` and real internet, confirm Auto mode resolves through the Gemini branch.
3. Switch to Offline through the tray Mode submenu, then dictate with a valid API key configured, and confirm the local model answers and no network call is made.
4. Tray Mode submenu reflects the active preference correctly (single checkmark, moves when you click a different entry) and persists across an app restart (`%LocalAppData%\VoiceCtrl\prefs.json`).

Interaction and personalization checks (the hook filters `LLKHF_INJECTED`, so synthetic keystrokes
cannot drive the hotkey and none of the first four can be automated):
1. Double-tap Ctrl in Notepad, speak, double-tap again. Text lands with no mouse involved.
2. Double-tap, speak, press Esc. Nothing is injected, and Esc still reaches Notepad.
3. Confirm the mic circle visibly tracks voice level while recording.
4. Double-tap again while a transcription is in flight: no second recording starts.
5. Add a term to `dictionary.txt`, save, dictate it without restarting, and confirm it comes back
   spelled as written. Repeat in Offline mode, which exercises `DictionaryCorrector` instead of the
   prompt path.
6. Add a snippet, confirm it expands, and confirm it expands identically in both modes.
7. Add a `profiles.json` entry for the app you are dictating into, confirm it takes effect, then
   remove one field from it and confirm that field falls back to the built-in behaviour rather than
   going blank.
8. Corrupt `profiles.json` deliberately (delete a brace), dictate, and confirm the dictation still
   succeeds with built-in behaviour and `log.txt` records the parse failure.

Run via `dotnet run --project src\VoiceCtrl` during development; final check against the published self-contained exe (`publish\VoiceCtrl.exe`) before considering this pass done, since single-file publish has its own gotchas (see config section) that `dotnet run` won't surface.

---

## Explicitly Out of Scope

Live-streaming/partial transcripts while speaking (needs a second, streaming ASR model resident at
all times; the level meter covers the "is it hearing me" question that motivated it), caret-following
bar placement (fixed bottom-center instead, since caret-position APIs are unreliable across arbitrary
apps), full settings GUI (raw `.env` and plain-text personalization files edited in Notepad instead),
custom TSF IME, auto-stop-on-silence detection, installer/MSI packaging, multi-hotkey customization
UI.

Considered and rejected on evidence rather than effort: offline self-correction handling and
offline spoken-number normalization, both of which need context a regex does not have; and
sherpa-onnx hotwords for the offline dictionary, which require `modified_beam_search` in place of
`greedy_search` and would cost latency on every dictation to serve a file most users leave empty.
The bounded post-ASR correction pass does the same job for the same cases at no cost when the file
is empty.

MP3 upload compression is implemented and measured (~13% of the WAV payload) but ships off, behind
`COMPRESS_UPLOAD`, because the free-tier quota ran out before a WAV-vs-MP3 transcript comparison
could be completed. Turning it on is a one-line change once that comparison is run.
