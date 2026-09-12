# VoiceCtrl

A Windows voice dictation utility: hotkey-triggered recording, transcription, cleanup, and paste at the cursor.

## Language

**Dictation**:
One full cycle of recording, transcription, cleanup, and paste, from trigger-start to trigger-stop.
_Avoid_: Recording, session

**Transcription**:
The text produced from a Dictation's audio, after cleanup.
_Avoid_: Output, result

**Mode**:
The transcription backend a Dictation uses: Auto, Online, or Offline.
_Avoid_: Backend, engine

**Overlay**:
The floating bar shown on screen for the duration of a Dictation, tracking mic level.
_Avoid_: Recording bar, widget, notch

**Hotkey**:
The key whose double-tap shows or hides the Overlay. Defaults to Ctrl; user-configurable. Never starts or stops a Dictation by itself — see Trigger Key.
_Avoid_: Trigger Key (a distinct, separate key), Trigger Style (retired term, see below)

**Trigger Key**:
An optional second key, distinct from the Hotkey, that starts and stops a Dictation hands-free. A tap does what clicking the Overlay's mic does — starts recording if the bar is visible and idle, stops/transcribes if recording. Holding it starts a Dictation immediately from any state (even with the bar hidden) and stops, transcribes, and hides on release (see Hold-to-Talk). Off until configured; the Hotkey's double-tap behavior is unaffected either way.
_Avoid_: Hotkey, Trigger Style (retired term, see below)

**Hold-to-Talk**:
The hold gesture on the Trigger Key: press and hold to start a Dictation immediately, release to stop, transcribe, and paste, then hide the Overlay. One of two gestures on the Trigger Key, alongside a tap.

_Retired: "Trigger Style" (a single Double-Tap-or-Hold-to-Talk choice) no longer describes the app — Hotkey and Trigger Key are independent, and Trigger Key's tap/hold gestures both live at once, not as alternatives._

**Personalization**:
The user's standing customizations that shape every Dictation's output: the Dictionary, Snippets, and Profiles.
_Avoid_: Settings, preferences

**Dictionary**:
The user's list of names, jargon, and product spellings used to correct Transcriptions.

**Snippet**:
A spoken shorthand (`trigger = expansion`) that expands during cleanup.
_Avoid_: Shortcut, macro (Macro collides with OS-level text expanders; Shortcut collides with Hotkey)

**Profile**:
The per-application override of Tone, Formatting, and Cleanup Level, keyed on the foreground process. An app with no Profile falls through to built-in behavior.

**Tone**:
A Profile's field controlling how casual or formal a Transcription reads. Edited only in the Hub's Profile Editor, as free text; never a live control on the Overlay.
_Avoid_: Tone Switcher (retired term, see below)

**Formatting**:
A Profile's field controlling structure: structured, prose, or none.

**Cleanup Level**:
A Profile's field controlling how aggressively filler words and disfluencies are removed: light, standard, or aggressive.

_Retired: "Tone Switcher" (a live click-to-cycle control on the Overlay for changing Tone mid-Dictation) — cut in favor of keeping the Overlay minimal and out of the way; Tone is a Profile Editor-only setting now, same as Formatting and Cleanup Level._

**Hub**:
The persistent window, opened from the tray, housing Dictations, the Personalization editors (Dictionary Editor, Snippet Editor, Profile Editor), and Settings. Added alongside the existing Settings and Personalize tray entries, not replacing them — the tray's "Settings..." entry still opens `.env` directly in Notepad as a raw fallback. Only exists in memory while open.
_Avoid_: Dashboard, Command Center

**Dictations**:
The Hub screen listing the user's last ~50 Dictations (text and timestamp only), searchable, with copy/paste-again. Renamed from History.
_Avoid_: History, Transcript log, history dashboard

**Settings**:
The Hub screen for app-wide configuration: the Hotkey and Trigger Key bindings, and the existing transcription Mode and Gemini API key (previously `.env`-only, edited via Notepad). Distinct from Personalization (Dictionary, Snippets, Profiles), which shapes Transcription output rather than app behavior.
