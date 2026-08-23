# VoiceCtrl

A Windows voice dictation utility. Double-tap Ctrl anywhere, speak, double-tap again, and the cleaned-up transcription is pasted at your cursor in whatever application has focus. Press Esc to throw the recording away. The mouse never enters the loop.

Three transcription modes:

- **Auto** (the default). Tries Gemini first, for the best accuracy and for self-correction handling, so "send it to John, actually no, Sarah" comes out as just "Sarah". Falls back to the offline model if there is no network or the request fails, and the overlay shows "Using offline mode" when that happens.
- **Online**. Always uses Google Gemini. Needs your own free API key and an internet connection.
- **Offline**. Runs entirely on your PC using a local speech model. No API key, no account, and no audio leaves your machine. It is CPU-only by design so it does not compete with your GPU. The first use downloads the model, roughly 650-700MB, once.

Switch modes at any time from the tray icon, under **Mode**.

## Install

1. Go to [Releases](../../releases) and download `VoiceCtrl-win-x64.zip`.
2. Extract it anywhere and run `VoiceCtrl.exe`.
3. Windows SmartScreen will most likely warn that "Windows protected your PC". This build is not code-signed, so click **More info**, then **Run anyway**.
4. On first launch a console window asks you to pick Online or Offline. For Online, paste your API key; a free one is available from [Google AI Studio](https://aistudio.google.com/apikey). Picking Online enables Auto mode, which is Gemini with offline fallback. Pick Offline if you want the app to never touch the network. Your choice is written to a local `.env` file next to the exe. Nothing is sent anywhere except your own Gemini API calls.
5. The app adds itself to the Start Menu as **VoiceCtrl**, so you can launch it later by typing the name.

VoiceCtrl then runs from the system tray. Double-tap Ctrl anywhere to start dictating.

To change the API key later, edit `.env`. The tray icon's Settings entry opens it in Notepad.

## Dictating

- **Start and stop.** Double-tap either Ctrl key. The bar opens already recording, and the mic circle tracks your voice level, so you can see that it is hearing you before you commit to a long sentence. Double-tap again to stop; the text is cleaned up and pasted where your cursor is.
- **Cancel.** Press Esc while recording. The clip is discarded, nothing is transcribed and nothing is pasted. Esc still reaches whatever app you were in.
- **Mouse.** Clicking the mic still starts and stops a recording, if you would rather.
- **Nothing is ever lost.** The last transcription stays available from the tray under **Copy last transcription**, and if a paste fails or the target window is elevated, the text is left on your clipboard and the bar tells you to press Ctrl+V yourself.

## Personalizing the output

Three files live in `%LOCALAPPDATA%\VoiceCtrl` and open from the tray icon under **Personalize**. Each is created with commented examples on first run, and each is re-read on the next dictation after you save it, so there is no restart and no settings dialog.

- **`dictionary.txt`.** Names, jargon and product spellings, one per line. Online mode passes them to the model as a spelling reference, so it can check a candidate against what it actually heard. Offline mode corrects the finished text against them, with a small edit budget that scales with the length of the term and a blocklist of common English words, so a near-miss is fixed and an ordinary word is never rewritten.
- **`snippets.txt`.** Spoken shorthand, written as `trigger = expansion`. Say "my work email", get the address. Matching is case-insensitive and fires on whole phrases only, longest trigger first, in a single pass, so an expansion that happens to contain another trigger is never re-expanded.
- **`profiles.json`.** Per-application overrides, keyed on the process name: `tone`, `formatting` (`structured`, `prose` or `none`), `cleanup` (`light`, `standard`, `aggressive`) and free-text `instructions`. Anything you leave out falls through to the built-in behaviour for that app, so you can correct one thing about one app without inheriting a frozen copy of everything else.

## Spoken commands

- "new line" and "new paragraph" work in both modes. They are guarded against ordinary speech, so "we are entering a new line of business" stays one sentence.
- Spoken punctuation ("comma", "period", "question mark") is handled by the model in Online mode. Offline it is off by default, behind `SPOKEN_PUNCTUATION=true` in `.env`, because those are ordinary English words and "the comma is missing" must not come out as "the , is missing".

## How it works

- **Hotkey.** Double-tap either Ctrl key to start and to stop, Esc to cancel. There is no chord, because Fn is not reliably interceptable on Windows.
- **Online transcription and cleanup.** One Gemini call transcribes the audio, drops mid-utterance corrections and retractions, removes filler words and fixes punctuation, all in a single pass. Your dictionary and the active app's profile ride along in the same request.
- **Offline transcription and cleanup.** A local Parakeet-TDT model, run through [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx), transcribes the audio. A local pass then removes filler words, collapses repeats, applies the spoken line-break commands, repairs the punctuation left behind by the words it deleted, and fixes capitalization. Self-correction handling is not available here, since it needs instruction-following that an on-device ASR model does not have.
- **Auto mode.** Every dictation tries Online first. If there is no network, or the Gemini request fails outright, that dictation falls back to the Offline path and the overlay shows "Using offline mode". Real API errors such as a bad key or an exhausted quota are surfaced rather than hidden.
- **Injection.** The clipboard is saved, set to the transcribed text, pasted with a simulated Ctrl+V, then restored.
- **Launcher.** A "VoiceCtrl" Start Menu shortcut carrying the app's own icon is created and refreshed on every launch, so it keeps pointing at the current exe after the app is moved or updated.

## Building from source

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Copy `.env.example` to `.env` and fill in a Gemini API key, or set `TRANSCRIPTION_MODE=Offline` to run without one.
3. `dotnet run --project src\VoiceCtrl`

`docs/design.md` covers the design and the implementation details.

## Attribution

Offline mode uses NVIDIA's [Parakeet-TDT-0.6B-v2](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2) (CC-BY-4.0) through [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) (Apache-2.0). See `THIRD-PARTY-NOTICES.md`.

## License

MIT. See `LICENSE`.
