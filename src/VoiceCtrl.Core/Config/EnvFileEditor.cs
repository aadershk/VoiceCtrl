namespace VoiceCtrl.Core.Config;

/// <summary>
/// Rewrites a subset of .env's KEY=value lines in place, preserving every other line (comments,
/// blank lines, unrelated keys, ordering) verbatim. Used by the Hub's Settings screen so editing
/// the Gemini API key never touches the rest of a hand-customized .env — mirrors the key/value
/// split <see cref="ConfigLoader"/> uses to read the file, just for writing instead.
/// </summary>
public static class EnvFileEditor
{
    /// <param name="lines">.env's current lines, in order.</param>
    /// <param name="updates">Key to new value, matched case-insensitively against each line's key.
    /// A key with no matching line is appended at the end.</param>
    public static string[] ApplyUpdates(IReadOnlyList<string> lines, IReadOnlyDictionary<string, string> updates)
    {
        var remaining = new Dictionary<string, string>(updates, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(lines.Count + remaining.Count);

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.Trim();
            int separatorIndex = trimmed.StartsWith('#') ? -1 : trimmed.IndexOf('=');

            if (separatorIndex > 0)
            {
                string key = trimmed[..separatorIndex].Trim();
                if (remaining.TryGetValue(key, out string? newValue))
                {
                    result.Add($"{key}={newValue}");
                    remaining.Remove(key);
                    continue;
                }
            }

            result.Add(line);
        }

        foreach ((string key, string value) in remaining)
        {
            result.Add($"{key}={value}");
        }

        return [.. result];
    }
}
