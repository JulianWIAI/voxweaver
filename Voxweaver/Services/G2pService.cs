using System.Text.RegularExpressions;

namespace Voxweaver.Services;

/// <summary>
/// Vowel-group heuristic syllable counter plus a basic consonant-cluster splitter.
/// Accuracy is sufficient for real-time UI feedback; swap in a full CMUdict lookup
/// for production-quality phoneme output.
/// </summary>
public class G2pService
{
    private static readonly Regex VowelGroup = new(@"[aeiouy]+", RegexOptions.Compiled);
    private static readonly Regex NonAlpha = new(@"[^a-z]", RegexOptions.Compiled);

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the total syllable count across all space-separated words in the input.
    /// </summary>
    public int CountSyllablesInText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                   .Sum(CountSyllables);
    }

    /// <summary>
    /// Splits every word in <paramref name="text"/> into individual syllables and
    /// returns them as a flat list, one entry per note.
    /// </summary>
    public List<string> GetSyllables(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        var syllables = new List<string>();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = SplitWord(word);
            syllables.AddRange(parts);
        }
        return syllables;
    }

    // ── Syllable counting ────────────────────────────────────────────────────

    private static int CountSyllables(string word)
    {
        var clean = NonAlpha.Replace(word.ToLowerInvariant(), "");
        if (clean.Length == 0) return 0;

        int count = VowelGroup.Matches(clean).Count;

        // Silent terminal 'e' (e.g. "love", "smile") — but not "the"
        if (clean.EndsWith('e') && count > 1)
            count--;

        // Syllabic 'le' after a consonant (e.g. "bottle", "little", "simple")
        if (clean.Length > 2 && clean.EndsWith("le") && !IsVowel(clean[^3]))
            count++;

        return Math.Max(1, count);
    }

    // ── Syllable splitting ────────────────────────────────────────────────────

    private string[] SplitWord(string word)
    {
        var clean = NonAlpha.Replace(word.ToLowerInvariant(), "");
        if (clean.Length == 0) return new[] { word };

        int count = CountSyllables(clean);
        if (count <= 1) return new[] { clean };

        var breaks = FindBreaks(clean);

        // breaks may be fewer than count-1 if the algorithm finds fewer split points
        var result = new List<string>();
        int prev = 0;
        foreach (int bp in breaks)
        {
            if (bp > prev)
                result.Add(clean[prev..bp]);
            prev = bp;
        }
        if (prev < clean.Length)
            result.Add(clean[prev..]);

        return result.Count > 0 ? result.ToArray() : new[] { clean };
    }

    /// <summary>
    /// Returns character positions at which syllable breaks should be inserted,
    /// using the standard V-CV / VC-CV consonant-cluster rule.
    /// </summary>
    private static List<int> FindBreaks(string word)
    {
        // Collect (start, end) of each vowel group
        var vowelGroups = new List<(int start, int end)>();
        int i = 0;
        while (i < word.Length)
        {
            if (IsVowel(word[i]))
            {
                int s = i;
                while (i < word.Length && IsVowel(word[i])) i++;
                vowelGroups.Add((s, i));
            }
            else i++;
        }

        var breaks = new List<int>();
        for (int g = 0; g < vowelGroups.Count - 1; g++)
        {
            int gapStart = vowelGroups[g].end;
            int gapEnd = vowelGroups[g + 1].start;
            int consonants = gapEnd - gapStart;

            int splitAt = consonants switch
            {
                0 => gapStart,       // Two adjacent vowel groups: split between them
                1 => gapStart,       // V-CV: keep consonant with the following vowel
                _ => gapStart + 1    // VC-CV…: split after the first consonant
            };

            breaks.Add(splitAt);
        }
        return breaks;
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
}
