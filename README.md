# VoiceCtrl

VoiceCtrl lets you talk instead of type. Tap a key twice, say what you want to write, tap it twice again, and the text appears wherever your cursor was, cleaned up and with filler words removed, in any program on your PC: Word, email, Slack, a browser, anything.

It runs quietly in the background (you'll see a small icon near your clock) and only shows itself when you're dictating.

## Before you install: a quick word on privacy

- Your voice recordings are **never saved**. VoiceCtrl only keeps the *text* it produced, never the audio.
- You can choose to run VoiceCtrl **fully offline** (see "Choosing how VoiceCtrl understands your speech" below), so nothing about what you say ever leaves your computer.
- Everything you type into VoiceCtrl to personalize it (see "Making VoiceCtrl sound like you" below) stays on your own PC, in a folder Windows keeps for your user account.

## Installing VoiceCtrl

1. Go to the [Releases page](../../releases) and download `VoiceCtrl-win-x64.zip`.
2. Right-click the zip file and choose **Extract All...**, then pick a folder to put it in (your Desktop or Documents both work fine).
3. Open that folder and double-click `VoiceCtrl.exe` to start it.
4. Windows will probably show a blue box saying "Windows protected your PC". This is completely normal for a small app like this one that isn't from a big company. It's not a sign of a virus. Click **More info**, then **Run anyway**.
5. The first time it runs, a short setup window appears and asks you one question: should VoiceCtrl understand your speech using the internet (Online) or entirely on your own PC (Offline)? Either answer is fine (see "Choosing how VoiceCtrl understands your speech" below if you're not sure), and you can always change your mind later. Follow the on-screen steps for whichever you pick.
6. Once setup finishes, VoiceCtrl is ready. From now on, you can also open it by typing "VoiceCtrl" into the Windows search box (bottom-left of your screen), since the setup added it there for you.

## Updating to a newer version

**Short answer: no, you don't have to delete anything, and you won't lose your settings or your dictation history either way.**

Everything you've personally added to VoiceCtrl (your Dictionary words, Snippets, app Profiles, and the history of what you've dictated) is stored separately from the app itself, in a folder Windows manages for your account. That folder never gets touched by installing a new version, no matter how you do it.

So when a new version comes out, you have two equally fine options:

- **Extract the new zip into the same folder as before**, overwriting the old files. This is the tidiest option, since there's nothing left over afterwards.
- **Extract the new zip into a brand new folder.** This also works perfectly, and your settings/history will be exactly as you left them. The only downside is the *old* folder and its `VoiceCtrl.exe` are still sitting on your hard drive, unused. They're harmless, but you may as well delete that old folder once you've confirmed the new one works, just to tidy up.

Either way, **just open the new `VoiceCtrl.exe`. You don't need to close the old version first.** VoiceCtrl automatically notices an older copy is still running, closes it for you, and takes its place, so there's never a moment with two copies running or any doubt about which one you're looking at. The "VoiceCtrl" shortcut in your Windows search box also automatically points at whichever copy you last opened, so you don't need to fix anything there yourself.

## Everyday use

### The little bar (the "Overlay")

- **Double-tap the Ctrl key** (either one) anywhere, on any app, to bring up a small dark bar at the bottom of your screen. It starts recording right away, so just start talking.
- **Double-tap Ctrl again** to stop. VoiceCtrl cleans up what you said (removing "um"s, fixing punctuation, etc.) and pastes it wherever your cursor is.
- You can also just **click the bar** with your mouse to start/stop, if you'd rather not use the keyboard shortcut.
- If a paste doesn't work for some reason (this can happen in certain secured windows), your words are automatically saved to your clipboard instead, and the bar will tell you to press Ctrl+V yourself.

### The VoiceCtrl window (the "Hub")

A window titled **VoiceCtrl** opens automatically whenever you start the app. This is where you can review what you've said and customize how VoiceCtrl behaves. Closing this window does **not** close VoiceCtrl itself: it keeps running quietly in the background (see the tray icon below), and you can always reopen the window from there. It has five sections, listed down the left side:

- **Dictations**: a list of the last 50 things you've dictated (just the text and the time, never audio), so you can find something you said earlier, copy it again, or delete it. You can search using the box in the top right.
- **Dictionary**: a list of names, brands, or unusual words that VoiceCtrl might otherwise mishear (e.g. your company's name, or a colleague's name). Add a word here once, and VoiceCtrl will get it right from then on.
- **Snippets**: shortcuts for things you type often. For example, teach it that saying "my email" should type out your actual email address in full.
- **Profiles**: lets you set a different writing style for specific programs. For example, you could make VoiceCtrl write more casually in a chat app and more formally in Word. **Note: this only affects the "Online" way of understanding your speech (see below); it has no effect when VoiceCtrl is working fully offline.**
- **Settings**: where you configure the keyboard shortcuts and choose Online/Offline mode (see the next two sections).

### The tray icon

Look for the VoiceCtrl icon near your clock, in the row of small icons at the bottom-right of your screen (click the little upward arrow there if you don't see it right away). Right-click it for a menu with quick options: reopen the VoiceCtrl window, pause/resume the app, switch modes, open your Dictionary/Snippets/Profiles directly, or quit VoiceCtrl entirely.

### Changing the keyboard shortcuts (optional)

By default, double-tapping Ctrl is all you need. If you'd like more hands-free control, open the Hub, go to **Settings → Hotkey**, and turn on the optional **Trigger Key**. Once it's on, that key can also work as a "press and hold to talk, release to finish" shortcut, handy if you don't want to double-tap at all.

**Important:** any change you make to these shortcuts only takes effect after you restart VoiceCtrl. To do that: right-click the tray icon, click **Quit**, then open VoiceCtrl again the same way you did the first time. The Settings screen will remind you of this whenever you make a change.

## Choosing how VoiceCtrl understands your speech

VoiceCtrl offers three ways to turn your voice into text. You can switch between them any time from the tray icon's **Mode** menu, or from **Settings → Transcription** in the Hub window.

- **Auto (recommended, and the default).** Uses the internet for the best accuracy when you have a connection, and automatically switches to working offline if you don't. Most people should just leave this as-is.
- **Online.** Always uses the internet (specifically Google's Gemini). This needs a free API key: a kind of password that lets VoiceCtrl use Google's service on your behalf. The setup steps walk you through getting one; it only takes a minute and doesn't cost anything for normal use.
- **Offline.** Works entirely on your own PC, with nothing sent over the internet at all. The first time you use this, VoiceCtrl downloads a speech model (about 700MB, a one-time download) so it has everything it needs stored locally.

You can add or change your Gemini API key at any time from **Settings → Transcription** in the Hub window (this used to require editing a technical file by hand, but it doesn't anymore). Changing the key requires a restart to take effect, the same as the shortcut changes above (right-click the tray icon → Quit, then reopen VoiceCtrl); the Settings screen will let you know when this applies.

## Frequently asked questions

**Do I need to close/uninstall the old version before installing a new one?**
No. See "Updating to a newer version" above. Just run the new `VoiceCtrl.exe`; it closes the old one automatically and takes over. Your data is always safe either way.

**Will I lose my Dictionary/Snippets/Profiles/history if I reinstall or move VoiceCtrl to a new folder?**
No. All of that is stored in a Windows-managed folder for your user account, completely separate from wherever you put `VoiceCtrl.exe`.

**Does VoiceCtrl record and store my voice?**
No, never. Only the resulting text is ever kept, and only the last 50 things you've said are kept at all (visible in the Hub's Dictations section).

**I changed the keyboard shortcut and it's not working.**
You need to restart VoiceCtrl for shortcut changes to apply. Right-click the tray icon, click Quit, then reopen the app.

## For anyone technical: building it yourself

If you'd rather build VoiceCtrl from its source code instead of downloading the release:

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Copy `.env.example` to `.env` and fill in a Gemini API key, or set `TRANSCRIPTION_MODE=Offline` to run without one.
3. `dotnet run --project src\VoiceCtrl`

`docs/design.md` covers the design and implementation details in full technical depth.

## Credits

Offline mode uses NVIDIA's [Parakeet-TDT-0.6B-v2](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2) (CC-BY-4.0) through [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) (Apache-2.0). See `THIRD-PARTY-NOTICES.md` for full details.

## License

MIT. See `LICENSE`.
