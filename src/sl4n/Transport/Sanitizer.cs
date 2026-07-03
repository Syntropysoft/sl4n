using System.Text;

namespace Sl4n;

/// <summary>
/// Strips ANSI escape sequences and control characters from string values before they reach a
/// transport — the log-injection safety boundary. Applied to the message and field values (not to
/// the exception blob, whose newlines carry the stack trace). Returns the same reference when there
/// is nothing to remove, so the common (clean) path allocates nothing.
/// </summary>
internal static class Sanitizer
{
    public static string Clean(string value)
    {
        if (value.Length == 0) return value;

        bool needs = false;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c < 0x20 || c == 0x7F) { needs = true; break; }
        }
        if (!needs) return value;

        StringBuilder sb = new(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\x1B')                       // ESC → drop the whole ANSI escape sequence
            {
                i = SkipAnsi(value, i);
                continue;
            }
            if (c < 0x20 || c == 0x7F) continue;   // drop other C0 control chars + DEL
            sb.Append(c);
        }
        return sb.ToString();
    }

    // Returns the index of the last char of the escape sequence starting at <paramref name="escIndex"/>.
    private static int SkipAnsi(string s, int escIndex)
    {
        int i = escIndex + 1;
        if (i < s.Length && s[i] == '[')           // CSI sequence: ESC [ params final
        {
            i++;
            while (i < s.Length && s[i] >= 0x20 && s[i] <= 0x3F) i++;      // parameter/intermediate bytes
            if (i < s.Length && s[i] >= 0x40 && s[i] <= 0x7E) return i;    // final byte — consume it
            return i - 1;
        }
        return escIndex;                            // lone ESC (or non-CSI) → drop just the ESC
    }
}
