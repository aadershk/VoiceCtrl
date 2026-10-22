# A cancelled recording over 3 seconds is kept, in memory, for 10 minutes

CONTEXT.md used to say "Cancel discards": nothing transcribed, nothing kept for Retry. That made
an accidental Esc or a slip on the pill's X the one way to lose a long dictation for good, with no
way back. Wispr Flow keeps such a recording and offers Undo, and the owners decided on
2026-10-17 (VED-69, built in VED-76) that VoiceCtrl does the same: a cancelled recording of more
than 3 seconds is kept, the pill offers Undo, and its text can be recovered from the Dictations
screen. This reverses the old rule, which is surprising and costly to reverse again (people will
rely on it, and the privacy wording changes), hence this record.

What was decided:

- **In memory only, never on disk.** It uses the same store as a Failed Dictation (a list of at
  most five, gone when VoiceCtrl quits), so "audio is never saved to disk" stays true and no new
  file holding someone's voice exists. The alternative, writing it to disk so it survives a crash
  or a restart, was rejected: a recording the user chose to throw away should not outlive the
  session.
- **A 10-minute window**, then the audio is dropped. Long enough to notice the slip and come back
  to the Dictations screen; short enough that a thrown-away recording does not linger.
- **Transcribed on demand.** Cancelling sends nothing anywhere. The text is made only when the
  user presses Undo or "Recover text", so cancelling still means "do not send my voice to Gemini".
  (Wispr Flow transcribes it straight away; we chose the more private order.)
- **Honours Keep Dictations.** With Keep Dictations set to None nothing is kept, and switching to
  None drops what is held. Settings says so in the Keep Dictations row.
- **Up to 3 seconds is still just dropped**, so a tap or a false start leaves no trace. A rewrite
  by voice (its audio is an instruction) and a recording with no sound in it are not kept either.

The same ticket adds a 20-minute recording limit (Wispr Flow's figure) that ends the recording as
if it had been finished, so its text is transcribed and pasted and not lost. The transcription
request's own deadline grows with the length of the recording (30 seconds plus half a second per
second of audio) so a 20-minute clip is not failed by the old flat 30 seconds.
