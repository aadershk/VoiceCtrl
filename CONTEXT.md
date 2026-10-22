# VoiceCtrl

A Windows voice dictation utility: hotkey-triggered recording, transcription, cleanup, and paste at the cursor.

## Language

**Dictation**:
One full cycle of recording, transcription, cleanup, and paste, from trigger-start to trigger-stop.
_Avoid_: Recording, session

**Transcription**:
The text produced from a Dictation's audio, after cleanup.
_Avoid_: Output, result

**Cancel**:
Throwing a Dictation in progress away, with Esc or the Overlay's X: nothing is pasted, and the Esc that cancels is not passed on to the app underneath. A recording of up to 3 seconds is simply dropped. A longer one is kept, in memory only, as a **Cancelled recording**, so an accidental Esc loses nothing: the pill offers Undo, which transcribes and pastes it after all (ADR-0004). Nothing is kept when Keep Dictations is None, for a rewrite by voice, or for a recording with no sound in it.
_Avoid_: Abort, stop (stop ends recording and goes on to transcribe)

**Cancelled recording**:
A recording of more than 3 seconds that was cancelled, kept in memory only, never on disk, for 10 minutes or until VoiceCtrl quits. It is listed on the Dictations screen with "Recover text" (transcribed then, never before, and added to the Dictations like any other), and offered on the pill as Undo for a few seconds after the Cancel. It shares the Failed Dictations' list and its cap of five, and becomes an ordinary Failed Dictation if a recovery fails.
_Avoid_: Discarded recording (nothing is discarded until the window ends)

**Recording limit**:
No recording runs longer than 20 minutes. The pill says "Under a minute left to record" at 19 minutes; at 20 the recording ends as if the user had finished it, so its text is transcribed and pasted, or kept for Retry if that fails, and never lost.
_Avoid_: Timeout (that is the transcription request's own, much longer, wait)

**Failed Dictation**:
A Dictation whose transcription did not succeed (no internet, timeout, service error). Its audio is kept in memory only, until a Retry succeeds, the user dismisses it, or VoiceCtrl quits; it is never written to disk. (A Cancelled recording is kept the same way, for 10 minutes.)
_Avoid_: Lost dictation, error

**Retry**:
Transcribing a Failed Dictation's kept audio again, from the Overlay or the Dictations screen, in the current Mode or explicitly Offline when the offline model is installed.

**Paste Again**:
Pasting the most recent Transcription at the cursor once more, from its own key combination or a Dictations row. Only ever the last Transcription or the chosen row, never a new Dictation.
_Avoid_: Repaste, paste last (as a name)

**Press Enter**:
An opt-in spoken ending ("press enter") that makes VoiceCtrl strip the phrase and send Enter after pasting, e.g. to send a chat message. Only honoured at the very end of a Dictation. Off by default.
_Avoid_: Send It, auto-send

**Sound Cues**:
Short sounds marking a Dictation's start, stop and completion. Off by default.

**Mute Other Audio**:
Lowering other apps' audio while a Dictation is recording and restoring it afterwards, including after a crash. On by default.
_Avoid_: Ducking (fine in code, not in UI copy)

**Mode**:
The transcription backend a Dictation uses: Auto, Online, or Offline.
_Avoid_: Backend, engine

**Overlay**:
The floating bar shown on screen for the duration of a Dictation, tracking mic level. Hidden when idle, unless the user turns on "Always show the Overlay" in Settings; it then rests as a small grey handle above the taskbar that becomes the Ready logo tile on hover.
_Avoid_: Recording bar, widget, notch

**Hotkey**:
The key whose double-tap shows or hides the Overlay. Defaults to Ctrl; user-configurable. Never starts or stops a Dictation by itself — see Trigger Key.
_Avoid_: Trigger Key (a distinct, separate key), Trigger Style (retired term, see below)

**Trigger Key**:
A second key or combination (one to three keys), distinct from the Hotkey, that starts and stops a Dictation hands-free. A tap does what clicking the Overlay's mic does — starts recording if the bar is visible and idle, stops/transcribes if recording. Holding it starts a Dictation immediately from any state (even with the bar hidden) and stops, transcribes, and hides on release (see Hold-to-Talk). On by default as Left Ctrl + Left Win (the user can change or remove it; a Trigger Key settings never stored reads as this default, one removed on purpose stays off). The Hotkey's double-tap behavior is unaffected either way.
_Avoid_: Hotkey, Trigger Style (retired term, see below)

**Hold-to-Talk**:
The hold gesture on the Trigger Key: press and hold to start a Dictation at the key press (no hold delay, so the first words are kept), release to stop, transcribe, and paste, then hide the Overlay. A press shorter than half a second is a tap and does nothing, silently: no cue, no message, the audio dropped. Pressing the keys twice within half a second of the first press locks the recording on hands-free (the Overlay shows the Listening look plus a padlock); the next press, the Overlay's tick or Esc ends it. The Rewrite key drops a tap the same way but never locks.

_Retired: "Trigger Style" (a single Double-Tap-or-Hold-to-Talk choice) no longer describes the app — Hotkey and Trigger Key are independent, and Trigger Key's tap/hold gestures both live at once, not as alternatives._

**Personalization**:
The user's standing customizations that shape every Dictation's output: the Dictionary, Snippets, and Profiles.
_Avoid_: Settings, preferences

**Dictionary**:
The user's list of names, jargon, and product spellings used to correct Transcriptions.

**Dictionary Suggestion**:
A word VoiceCtrl proposes adding to the Dictionary (e.g. a name the user says often), found in their stored Dictations. Suggestions are opt-in, never added without the user confirming, and never generated in Offline Mode.
_Avoid_: Auto-learning (nothing is added automatically)

**Snippet**:
A spoken shorthand (`trigger = expansion`) that expands during cleanup.
_Avoid_: Shortcut, macro (Macro collides with OS-level text expanders; Shortcut collides with Hotkey)

**App kind**:
Which of six kinds of app a Dictation lands in, each with its own built-in layout and, but for AI prompts, its own Style, Online and Offline alike. **Email** (Outlook, Thunderbird, Gmail and Outlook in a browser): Email Layout. **Work chat** (Slack, Teams, Google Chat) and **Personal chat** (WhatsApp, Telegram, Signal, Messenger, Discord), and their web versions: written like a typed text, one paragraph in a Personal chat and short paragraphs by topic in a Work chat. **Document** (Word, Notion, OneNote, Obsidian, Google Docs): full sentences in paragraphs, a spoken heading on its own line. **AI prompt** (Cursor, VS Code, terminals, Claude Code, ChatGPT, Claude, Gemini): several requests or steps as a list, technical terms kept exactly. **Other**: clean sentences. A list the speaker spells out is a list in every kind. A desktop app is known by its process; a browser by the site its window title names, matched on the PC and never logged or sent. The Style screen can move an app to another kind, and a Profile overrides any part of it.
_Avoid_: category, style (a Style is what a kind's text reads like, not the kind)

**Style**:
How a Transcription reads in a kind of app: its capitals, closing punctuation and register, chosen per App kind on the Style screen, or for one app in its Profile, which wins. **Formal** writes contractions out ("I will") and casual words formally ("yes", "going to") and keeps full stops; **Casual** drops the full stop that closes a line; **Very casual** does that and starts sentences lowercase, while "I" and names keep their capitals; **Excited** ends sentences with "!". Defaults: Casual for Work chat and Personal chat, Formal for Email, Document and Other. AI prompts have none, so their words and capitals stay as said. Done in code on either engine's text after the layout, so Online, Offline and a message too short for Smart cleanup read alike; Word for word keeps every word, so there Formal changes no words. Never adds content.
_Avoid_: Tone (a Profile's old free-text field, now How I write here), register

**Profile**:
The per-application override of Style, How I write here, Formatting, Cleanup Level, and Email Layout, keyed on the foreground process. An app with no Profile falls through to the defaults (e.g. the Default cleanup chosen in Settings) and then to its App kind's built-in treatment.

**How I write here**:
A Profile's short free-text note for the model about that one app ("keep it to one line", "bullet my updates"), up to 200 characters. It replaced the Profile Editor's Tone box (ticket 56): a Profile's old Tone text shows here and moves here when the Profile is saved. Rule 4 (never add content) still wins over it.

**Tone**:
A Profile's old free-text field for how casual or formal a Transcription reads, superseded by Style and How I write here. Still honoured in a hand-written profiles.json; "none" there turns the app's built-in tone hint off. Never a live control on the Overlay.
_Avoid_: Tone Switcher (retired term, see below)

**Formatting**:
A Profile's field controlling structure: structured (an AI prompt's lists), prose (a chat message's paragraph), paragraphs (a document's), or none. Unset, it follows the App kind.

**Smart cleanup**:
Offline's text editing step: after the speech model and the plain rules, a small language model on this PC follows spoken corrections, sets out lists, removes fillers the rules miss, and applies a Profile's Tone, Formatting and Email Layout, as Online does in its one pass. It edits what was said and never adds to it: its answer is checked word by word against the transcript, and anything it adds or answers is thrown away and the rule-cleaned text is pasted instead. On by default, but it does nothing until its model (Standard or Better) is downloaded; it costs seconds after the Hotkey is released and none on a short, plain sentence.
_Avoid_: AI cleanup, rewrite (rewriting selected text is a separate feature, see Rewrite by voice)

**Rewrite by voice**:
Holding the **Rewrite key** (Left Ctrl + Left Shift unless changed), saying what to do to a text ("make this shorter", "turn this into bullets", "translate to Dutch") and letting go: the result replaces the text. The text is the selection, or the last Dictation when nothing is selected; in an email app with nothing selected the instruction may instead be an **Email request**. Online, Gemini hears the instruction and applies it in one call; Offline, the speech model writes the instruction down and the Smart cleanup language model applies it (about 3 s, longer for a long text). Unlike Smart cleanup it is meant to change words, so there is no word check: what bounds it is that the text before is kept as the **Original** (Restore original) and the paste is one undo step. A rewrite that is too slow, unusable or fails leaves the text as it was. Selecting and replacing go through the clipboard and the keyboard like a paste, so it cannot reach an app running as administrator.
_Avoid_: Command Mode (Wispr Flow's name), voice edit (a 4.1 idea: editing by voice while dictating)

**Rewrite key**:
The held key or combination (one to three keys, like the Trigger Key) that starts a rewrite by voice. On by default as Left Ctrl + Left Shift, which Windows also uses to switch the input language on a PC with several keyboard layouts and which many apps use to start shortcuts (Ctrl + Shift + T), so another key pressed before the hold begins, or during it, makes it a shortcut and not a rewrite (a rewrite already recording is thrown away). A quick tap does nothing, so Windows' language switch still works; only a real hold keeps Windows from acting on it. Never the Hotkey or the Trigger Key's keys.
_Avoid_: Trigger Key (that dictates)

**Original**:
The text of a Dictation before Smart cleanup edited it (or, for a rewrite by voice, the text before it was rewritten), kept beside the edited text for as long as the Dictation is kept, and offered back with Restore original on the Dictations screen. Absent when Smart cleanup left the text alone. Online keeps one too: Gemini returns what was said beside the edited text in the same call, only when it changed more than punctuation and capitals.

**Engine record**:
An off-by-default switch in Settings, Privacy. While on, each Offline Dictation leaves one entry in `engine-record.json` under the data root: whether the check for mis-heard words ran, the `wrong -> right` word pairs it applied, why Smart cleanup's answer was refused (the kind only), whether the built-in vocabulary rewrote a word (the pair), time spent on the language model and the total time. Never the Dictation, its Original, an app name or a window title. Stays on this PC, keeps the last 500 entries, is added to Copy diagnostics (pairs scrubbed like a setting's value) only while on, and is deleted when turned off. Made for the trial (VED-77); it ships in the app.
_Avoid_: telemetry, analytics (nothing is sent anywhere)

**Cleanup Level**:
How aggressively filler words and disfluencies are removed: verbatim (word for word, nothing removed), light, standard, or aggressive (labelled "Word for word" and "Thorough" in the UI). Set app-wide as the Default cleanup in Settings (standard by default), and overridable per app as a Profile field.

**Email Layout**:
Laying a Transcription out as an email: the greeting on its own line, the body in short paragraphs, the sign-off and the sender's name on their own lines, a blank line between each part. Structural only: it never invents content, and uses a greeting, sign-off or name only if one was spoken (an Email request, below, is the one exception). On by default in email apps (App kind) and off elsewhere; a Profile can turn it either way. The greeting and sign-off lines are set in code on either engine's text; the paragraphs come from Gemini Online, and Offline from Smart cleanup once it is installed.
_Avoid_: Shape (the pre-v4 working name)

**Email request**:
An instruction said while holding the **Rewrite key** with nothing selected in an email app (App kind Email, Email Layout on) that asks for a new email rather than an edit: "write an email to Mark thanking him for his time", "reply to Ben saying Tuesday works", "tell my landlord the rent will be late". VoiceCtrl writes the whole email and pastes it at the caret: the greeting to the person the request names (no one named: "Hello,"), a body written by Gemini Online or the Smart cleanup model Offline, and the email Style's sign-off with the Style screen's **Your name** under it (none if it is empty). Plain dictation is never read for requests, so dictated words are never rewritten (owner, 2026-10-20). With the key held, the instruction is heard first and told apart from an edit in code: one that names a text ("make it shorter", "translate this") or opens with an edit verb stays an edit of the last Dictation, as before; one that opens with an order to write, reply, tell or email someone is a request. A guard refuses a body that adds a number, date, day, name, link, verse or the model's own voice the request did not give; then, or when the model is too slow, nothing is pasted and the pill says so. The pill says "Writing email" while it writes. The words said are the Dictation's **Original**, and the email is one paste in the app's undo. The one exception to "never adds content" (ADR-0005).
_Avoid_: compose mode, AI email, smart reply, Instruct (Wispr Flow's name)

_Retired: "Tone Switcher" (a live click-to-cycle control on the Overlay for changing Tone mid-Dictation) — cut in favor of keeping the Overlay minimal and out of the way; Tone is a Profile Editor-only setting now, same as Formatting and Cleanup Level._

**Hub**:
The persistent window, opened from the tray, housing Dictations, the Personalization editors (Dictionary Editor, Snippet Editor, Profile Editor), and Settings. Added alongside the existing Settings and Personalize tray entries, not replacing them — the tray's "Settings..." entry still opens `.env` directly in Notepad as a raw fallback. Only exists in memory while open.
_Avoid_: Dashboard, Command Center

**Quick Find**:
The sidebar's own search box, always visible above the nav items, that jumps to any Hub screen or named Settings row by typing part of its name — a navigation aid, not a content search. Distinct from the Dictations screen's "Search Dictations" box, which searches dictation text and never leaves the Dictations screen.
_Avoid_: Search (ambiguous with Search Dictations), Command Palette (heavier connotation than this is)

**Dictations**:
The Hub screen listing the user's recent Dictations (text, timestamp and the app it went into, never audio), kept according to Retention, searchable, with Copy and Paste Again. A Failed Dictation also appears here with Retry until it succeeds or is dismissed, and a Cancelled recording with Recover text. Renamed from History.
_Avoid_: History, Transcript log, history dashboard

**Retention**:
How long Dictations are kept: the last 50 (default), the last 24 hours, or none. Labelled "Keep Dictations" in Settings. None also means no Cancelled recording is kept.
_Avoid_: History settings

**Settings**:
The Hub screen for app-wide configuration: the Hotkey and Trigger Key bindings, and the existing transcription Mode and Gemini API key (previously `.env`-only, edited via Notepad). Distinct from Personalization (Dictionary, Snippets, Profiles), which shapes Transcription output rather than app behavior.

**Theme**:
The Hub and Setup window's Light, Dark, or System appearance selection, set in Settings. System follows the OS theme live while the Hub is open. Also drives the tray's right-click context menu's colors, resolved fresh each time it opens rather than tracked live. Distinct from Mode (the transcription backend), from Ink & Ember (the visual language Theme is a setting within), and from the taskbar's own light/dark state below, which Theme has no say over.
_Avoid_: Mode (collides with transcription Mode)

**Taskbar theme**:
Windows' own light/dark state for the taskbar specifically (`SystemUsesLightTheme`), independent of both Theme above and the apps' light/dark default (`AppsUseLightTheme`) — Windows lets a user run a light taskbar with dark apps, or vice versa, via Custom personalization mode. Exists only to pick which tray icon asset (light- or dark-taskbar tint) renders correctly against the current taskbar; it is not a user-facing setting and has no Light/Dark/System picker of its own.
_Avoid_: Theme (this is a different axis Theme doesn't control)

**Ink & Ember**:
The app's visual language from v4.0. Both Themes share one layout, one type system and one set of shapes; switching Theme changes only colour. Light is warm cream and charcoal (the ember); Dark is the cool ink of the logo's tile (the ink). The coral of the logo's bars is the single accent in both. Surfaces are soft and rounded, and the Hub gives the latest Transcription one editorial moment. The Overlay stays a compact, quiet bar whose level meter echoes the logo's three bars. The exact typefaces, colours and scales are implementation choices tracked in specs, not part of this definition.
_Avoid_: Quiet Ledger (the pre-v4 visual language it replaces), Warm Signal, Quiet Signal (candidate directions it was assembled from)
