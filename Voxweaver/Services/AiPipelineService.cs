using System.Text.Json;
using Voxweaver.Models;
using Voxweaver.ViewModels;

namespace Voxweaver.Services;

// ── Result types ──────────────────────────────────────────────────────────────

public class ValidationIssue
{
    public string LineId { get; init; } = string.Empty;
    public int RequiredSyllables { get; init; }
    public int ActualSyllables { get; init; }
    public string Lyrics { get; init; } = string.Empty;

    public override string ToString() =>
        $"[{LineId}] expected {RequiredSyllables} syllable(s), got {ActualSyllables} — \"{Lyrics}\"";
}

public class IngestResult
{
    /// <summary>lineId → syllable-normalised lyrics (hyphens converted to spaces).</summary>
    public Dictionary<string, string> NormalisedLyrics { get; } = new();

    /// <summary>Lines whose syllable count did not match the MIDI phrase.</summary>
    public List<ValidationIssue> Issues { get; } = new();

    public bool HasIssues => Issues.Count > 0;
}

// ── Service ───────────────────────────────────────────────────────────────────

public class AiPipelineService
{
    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true };

    // ── Export ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Serialises the phrase list into the JSON payload the LLM expects.
    /// </summary>
    public string BuildExportJson(string theme, IReadOnlyList<PhraseViewModel> phrases)
    {
        var payload = new AiExportPayload
        {
            Theme = theme,
            Sequences = phrases.Select((p, i) => new SequenceEntry
            {
                LineId = LineId(i),
                SyllableCount = p.NoteCount
            }).ToList()
        };

        return JsonSerializer.Serialize(payload, WriteOpts);
    }

    // ── Ingest ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the LLM's <c>{ "Line_01": "Sha-dows on the wall" }</c> response,
    /// validates syllable counts, and returns normalised lyrics ready for USTX export.
    /// </summary>
    /// <remarks>
    /// Syllable counting splits on hyphens and spaces so the LLM can annotate
    /// boundaries explicitly: "Watch-ing ev-ery move you make" = 7 tokens.
    /// Normalised lyrics replace hyphens with spaces so each token is one word
    /// and the existing G2pService counts it as exactly one syllable.
    /// </remarks>
    public IngestResult IngestResponse(string json, IReadOnlyList<PhraseViewModel> phrases)
    {
        Dictionary<string, string> response;
        try
        {
            response = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                       ?? throw new InvalidOperationException("Response deserialised to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON in LLM response: {ex.Message}", ex);
        }

        var result = new IngestResult();

        foreach (var (lineId, rawLyrics) in response)
        {
            if (!TryParseIndex(lineId, out int idx) || idx >= phrases.Count)
            {
                Console.Error.WriteLine($"[AiPipeline] Unknown lineId '{lineId}' — skipped.");
                continue;
            }

            int required = phrases[idx].NoteCount;
            int actual   = CountTokens(rawLyrics);

            if (actual != required)
            {
                var issue = new ValidationIssue
                {
                    LineId = lineId,
                    RequiredSyllables = required,
                    ActualSyllables = actual,
                    Lyrics = rawLyrics
                };
                result.Issues.Add(issue);
                Console.Error.WriteLine($"[AiPipeline] MISMATCH {issue}");
                // Still store it — the caller decides whether to apply or skip.
            }

            // Normalise: replace hyphens with spaces so each token = 1 syllable word.
            result.NormalisedLyrics[lineId] = rawLyrics.Replace('-', ' ');
        }

        return result;
    }

    // ── Syllable parsing for USTX ─────────────────────────────────────────────

    /// <summary>
    /// Splits LLM-annotated lyrics into individual syllable tokens.
    /// "Sha-dows on the wall" → ["Sha", "dows", "on", "the", "wall"]
    /// </summary>
    public static List<string> ParseSyllables(string llmLyrics) =>
        llmLyrics.Split(new[] { '-', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static string LineId(int zeroBasedIndex) => $"Line_{zeroBasedIndex + 1:D2}";

    public static bool TryParseIndex(string lineId, out int zeroBasedIndex)
    {
        var parts = lineId.Split('_');
        if (parts.Length == 2 && int.TryParse(parts[1], out int n) && n >= 1)
        {
            zeroBasedIndex = n - 1;
            return true;
        }
        zeroBasedIndex = -1;
        return false;
    }

    private static int CountTokens(string lyrics) =>
        lyrics.Split(new[] { '-', ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
}
