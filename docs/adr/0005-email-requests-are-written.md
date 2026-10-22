# An email request said with the Rewrite key is written, as the one exception to "never add content"

Every engine rule so far keeps the user's words: Smart cleanup's answer is checked word by word and
thrown away if it adds anything, Gemini's prompt says "do not add information", Email Layout uses a
greeting or sign-off only if one was spoken, and a Style never adds content. Ticket 57 (VED-80) adds
one deliberate exception: in an email app, an instruction like "write an email to Mark thanking him
for his time" gets the email written, with a greeting, a body and a sign-off nobody said. That
reverses a rule other parts of the app and its copy rely on, so it is recorded here.

What was decided (owner, 2026-10-20):

- **Only with the Rewrite key, never in plain dictation.** The first build read every Dictation in an
  email app for requests, using rules on the words. On three blind sets it rewrote up to 1 in 40
  request-like dictated emails and missed a quarter to two fifths of the requests. Research
  (`.scratch/v4-ship/research/57-dictation-vs-command.md`) found that no comparable product guesses
  "write this for me" from free speech by default: Wispr Flow, Willow, Aqua and Superwhisper all put
  it behind a key or mode, and Wispr's own in-stream classifier ships switched off and falls back to
  dictation on any doubt. The owner chose: dictation is never rewritten by construction, and the
  Rewrite key, already "do what I say", writes the email when nothing is selected in an email app.
- **Request or edit, told apart in code, with edits winning ties.** With nothing selected, the
  instruction is heard first. One that names a text ("make it shorter", "translate this") or opens
  with an edit verb edits the last Dictation, as the Rewrite key always did; one that opens with an
  order to write, reply, tell or email someone is a request. Both are commands the user chose to
  give, so a wrong guess never touches plain dictation, and it is visible and undoable.
- **Names only from the user.** Code writes the greeting to the person the request names and the
  sign-off with the Style screen's "Your name"; the model writes the body only.
- **A guard on the body.** It may add ordinary words ("I really appreciate it") but never a number,
  a date or day, a name, a link, verse, or the model speaking as itself, that the request did not
  give. A refused body, a timeout or no model means nothing is pasted and the pill says so. We
  rejected a word-by-word check like Smart cleanup's because an email must have words that were not
  said, and rejected a language model deciding what is a request because its errors cannot be bounded.
- **Always reversible.** The words said are kept as the Dictation's Original, and the email lands as
  one paste, so Ctrl+Z takes it out.
