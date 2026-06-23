using Voxweaver.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;

namespace Voxweaver.Services;

// ── YAML guard: force-quote any string that YAML would misread as a boolean ──
// Prevents "true"/"false" lyrics from round-tripping as C# bool → "True".

public sealed class ForceStringQuotingEmitter : ChainedEventEmitter
{
    private static readonly HashSet<string> YamlBooleans =
        new(StringComparer.OrdinalIgnoreCase)
        { "true", "false", "yes", "no", "on", "off", "null", "~" };

    public ForceStringQuotingEmitter(IEventEmitter next) : base(next) { }

    public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
    {
        if (eventInfo.Source.Type == typeof(string) &&
            eventInfo.Source.Value is string s &&
            (s == "" || YamlBooleans.Contains(s)))
        {
            eventInfo.Style = ScalarStyle.DoubleQuoted;
        }
        base.Emit(eventInfo, emitter);
    }
}

// ── USTX POCO graph ──────────────────────────────────────────────────────────

public class UstxProject
{
    public string Name { get; set; } = "Voxweaver Export";
    public string Comment { get; set; } = "";
    public string OutputDir { get; set; } = "Vocal";
    public string CachesDir { get; set; } = "UCache";
    public string UstxVersion { get; set; } = "0.6";
    public int Resolution { get; set; } = 480;
    public double Bpm { get; set; } = 120.0;
    public int BeatPerBar { get; set; } = 4;
    public int BeatUnit { get; set; } = 4;
    public Dictionary<string, UstxExpression> Expressions { get; set; } = new();
    public List<UstxTrack> Tracks { get; set; } = new();
    public List<UstxVoicePart> VoiceParts { get; set; } = new();
    public List<object> WaveParts { get; set; } = new();
}

public class UstxExpression
{
    public string Name { get; set; } = "";
    public string Abbr { get; set; } = "";
    public string Type { get; set; } = "Curve";
    public int Min { get; set; }
    public int Max { get; set; }
    public int DefaultValue { get; set; }
    public bool IsFlag { get; set; }
}

public class UstxTrack
{
    public string TrackName { get; set; } = "Track 0";
    public string Singer { get; set; } = "TIGER DS";
    public string Phonemizer { get; set; } = "OpenUtau.Core.DiffSinger.DiffSingerEnglishPhonemizer";
    public UstxRendererSettings RendererSettings { get; set; } = new();
}

public class UstxRendererSettings
{
    public string Renderer { get; set; } = "DIFFSINGER";
}

public class UstxVoicePart
{
    public string Name { get; set; } = "Part 1";
    public string Comment { get; set; } = "";
    public int TrackNo { get; set; } = 0;
    public long Position { get; set; } = 0;
    public List<UstxNote> Notes { get; set; } = new();
    public List<UstxCurve> Curves { get; set; } = new();
}

public class UstxCurve
{
    public string Abbr { get; set; } = "vel";
    public List<long> Xs { get; set; } = new();
    public List<int> Ys { get; set; } = new();
    public bool IsSample { get; set; } = false;
}

// OpenUtau requires pitch.data (non-empty) + snap_first and a fully-populated vibrato.
// phoneme_expressions must be omitted (not an empty list); OpenUtau initialises it from
// null after phonemisation so the per-phoneme index access at UNote.Validate:105 is safe.
public class UstxNote
{
    public long Position { get; set; }
    public long Duration { get; set; }
    public int Tone { get; set; }
    public string Lyric { get; set; } = "la";
    public UstxPitch Pitch { get; set; } = new();
    public UstxVibrato Vibrato { get; set; } = new();
    public Dictionary<string, object> NoteExpressions { get; set; } = new();
    // Intentionally absent: phoneme_expressions must not be written as an empty list.
}

public class UstxPitch
{
    public List<UstxPitchPoint> Data { get; set; } = new();
    public bool SnapFirst { get; set; } = true;
}

public class UstxPitchPoint
{
    public float X { get; set; }
    public float Y { get; set; }
    public string Shape { get; set; } = "l";
}

public class UstxVibrato
{
    public double Length { get; set; } = 0;
    public double Period { get; set; } = 175;
    public double Depth { get; set; } = 25;
    public double In { get; set; } = 10;
    public double Out { get; set; } = 10;
    public double Shift { get; set; } = 0;
    public double Drift { get; set; } = 0;
}

// ── Export service ────────────────────────────────────────────────────────────

public class UstxExportService
{
    private const int UstxResolution = 480;

    // Cluster-detection thresholds (in USTX ticks, after correct tick conversion).
    private const int TinyThreshold  = 40;   // notes shorter than this are pickup consonants
    private const int MainThreshold  = 200;  // notes at least this long are syllable beats
    private const int ClusterGapMax  = 20;   // max gap between last tiny note and main note
    private const int ChordWindow    = 20;   // notes whose positions are within this window are chords

    // Duration enforcement.
    private const int SoftMinDuration = 480; // one beat — applied after merge
    private const int HardMinDuration = 240; // absolute floor even after gap-cap

    private readonly G2pService _g2p = new();

    // Mutable working note used by the post-processing pipeline.
    private sealed class WorkNote
    {
        public long   Position { get; set; }
        public long   Duration { get; set; }
        public int    Pitch    { get; set; }
        public string Syllable { get; set; } = "la";
    }

    public void Export(
        string outputPath,
        string projectName,
        double bpm,
        short  midiTpqn,
        IEnumerable<(Phrase phrase, string lyrics)> phraseLyricPairs)
    {
        var project = new UstxProject
        {
            Name       = projectName,
            Bpm        = Math.Round(bpm, 3),
            Resolution = UstxResolution,
            Expressions = BuildDefaultExpressions(),
            Tracks     = new List<UstxTrack> { new() { TrackName = "Vocal" } }
        };

        // ── 1. Convert MIDI ticks → USTX ticks (Bug 1 fix: no extra ×4 scale) ─
        // For a 960-TPQN MIDI, scale = 0.5.  For 480-TPQN, scale = 1.0.
        double scale = (double)UstxResolution / midiTpqn;

        var raw = new List<WorkNote>();
        foreach (var (phrase, lyrics) in phraseLyricPairs)
        {
            var syllables = _g2p.GetSyllables(lyrics);
            for (int i = 0; i < phrase.Notes.Count; i++)
            {
                var mn = phrase.Notes[i];
                raw.Add(new WorkNote
                {
                    Position = (long)(mn.TickStart    * scale),
                    Duration = Math.Max(1, (long)(mn.TickDuration * scale)),
                    Pitch    = mn.Pitch,
                    Syllable = SanitizeLyric(i < syllables.Count ? syllables[i] : "la")
                });
            }
        }

        // ── 2. Merge tiny pickup clusters into the following main note (Bug 2) ─
        var merged = MergeClusters(raw);

        // ── 3. Drop polyphonic duplicates (keep the longest note per window) ───
        merged = RemoveChords(merged);

        // ── 4. Enforce duration floor; cap to prevent any overlap ─────────────
        EnforceDurations(merged);

        // ── 5. Build UstxNote list and velocity curve ─────────────────────────
        var ustxNotes = new List<UstxNote>();
        var velXs     = new List<long>();
        var velYs     = new List<int>();
        int total     = merged.Count;

        for (int i = 0; i < total; i++)
        {
            var wn = merged[i];
            ustxNotes.Add(new UstxNote
            {
                Position = wn.Position,
                Duration = wn.Duration,
                Tone     = wn.Pitch,
                Lyric    = wn.Syllable,
                Pitch    = new UstxPitch
                {
                    Data = new List<UstxPitchPoint>
                    {
                        new() { X = -40f, Y = 0f, Shape = "l" },
                        new() { X =   0f, Y = 0f, Shape = "l" }
                    },
                    SnapFirst = true
                }
            });

            velXs.Add(wn.Position);
            velYs.Add(HumanizedVelocity(i, total));
        }

        project.VoiceParts.Add(new UstxVoicePart
        {
            Name     = "Vocal",
            Position = 0,
            Notes    = ustxNotes,
            Curves   = new List<UstxCurve>
            {
                new() { Abbr = "vel", Xs = velXs, Ys = velYs }
            }
        });

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .WithEventEmitter(next => new ForceStringQuotingEmitter(next))
            .Build();

        File.WriteAllText(outputPath, serializer.Serialize(project));
    }

    // ── Post-processing pipeline ──────────────────────────────────────────────

    // Finds runs of tiny pickup notes immediately before a main note and merges
    // them into the main note, extending its start position back to the first
    // pickup and combining all syllables.
    private static List<WorkNote> MergeClusters(List<WorkNote> notes)
    {
        var result = new List<WorkNote>();
        int i = 0;

        while (i < notes.Count)
        {
            if (notes[i].Duration >= TinyThreshold)
            {
                result.Add(notes[i++]);
                continue;
            }

            // Collect consecutive tiny notes.
            var cluster = new List<WorkNote> { notes[i] };
            int j = i + 1;
            while (j < notes.Count && notes[j].Duration < TinyThreshold)
            {
                long betweenGap = notes[j].Position - (notes[j - 1].Position + notes[j - 1].Duration);
                if (betweenGap > ClusterGapMax) break;
                cluster.Add(notes[j]);
                j++;
            }

            // Check whether the note right after the cluster is a valid main note.
            bool didMerge = false;
            if (j < notes.Count && notes[j].Duration >= MainThreshold)
            {
                long lastEnd   = cluster[^1].Position + cluster[^1].Duration;
                long gapToMain = notes[j].Position - lastEnd;
                if (gapToMain < ClusterGapMax)
                {
                    var main   = notes[j];
                    long newEnd = main.Position + main.Duration;
                    main.Position  = cluster[0].Position;
                    main.Duration  = newEnd - main.Position;
                    main.Syllable  = string.Join(" ",
                        cluster.Select(n => n.Syllable)
                               .Append(main.Syllable)
                               .Where(s => !string.IsNullOrEmpty(s)));
                    result.Add(main);
                    i = j + 1;
                    didMerge = true;
                }
            }

            if (!didMerge)
            {
                // No valid main note — emit the tiny notes individually.
                foreach (var t in cluster) result.Add(t);
                i = j;
            }
        }

        return result;
    }

    // Within each chord window (notes whose positions are within ChordWindow ticks
    // of each other) keep only the longest note and combine distinct syllables.
    private static List<WorkNote> RemoveChords(List<WorkNote> notes)
    {
        var result = new List<WorkNote>();
        int i = 0;

        while (i < notes.Count)
        {
            int j = i + 1;
            while (j < notes.Count &&
                   Math.Abs(notes[j].Position - notes[i].Position) < ChordWindow)
            {
                j++;
            }

            var group = notes.Skip(i).Take(j - i)
                             .OrderByDescending(n => n.Duration)
                             .ToList();
            var best = group[0];
            best.Syllable = string.Join(" ",
                group.Select(n => n.Syllable)
                     .Where(s => !string.IsNullOrEmpty(s))
                     .Distinct());
            result.Add(best);
            i = j;
        }

        return result;
    }

    // Apply soft minimum (480), then cap each note to the gap before the next
    // note to guarantee zero overlaps, with a hard floor of 240.
    private static void EnforceDurations(List<WorkNote> notes)
    {
        foreach (var n in notes)
            n.Duration = Math.Max(n.Duration, SoftMinDuration);

        for (int i = 0; i < notes.Count - 1; i++)
        {
            long gap = notes[i + 1].Position - notes[i].Position;
            if (gap > 0)
                notes[i].Duration = Math.Min(notes[i].Duration, gap - 1);
            notes[i].Duration = Math.Max(notes[i].Duration, HardMinDuration);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Multi-frequency sine contour: deterministic but varied enough to sound
    // natural.  No Random needed, so exports are reproducible.
    private static int HumanizedVelocity(int index, int total)
    {
        double phase = index / Math.Max(total - 1.0, 1) * 2 * Math.PI;
        int v = (int)(100
                + 10 * Math.Sin(phase * 3.7 + 0.5)
                +  5 * Math.Sin(phase * 7.1));
        return Math.Clamp(v, 85, 115);
    }

    // Guard against YAML boolean misinterpretation and empty lyric fields.
    private static readonly HashSet<string> YamlReserved =
        new(StringComparer.OrdinalIgnoreCase)
        { "true", "false", "yes", "no", "on", "off", "null", "~" };

    private static string SanitizeLyric(string raw)
    {
        var t = (raw ?? "").Trim();
        return (t == "" || YamlReserved.Contains(t)) ? "la" : t;
    }

    private static Dictionary<string, UstxExpression> BuildDefaultExpressions() => new()
    {
        ["vel"]  = new UstxExpression { Name = "velocity (curve)",       Abbr = "vel",  Min = 0,     Max = 200,  DefaultValue = 100 },
        ["vol"]  = new UstxExpression { Name = "volume (curve)",          Abbr = "vol",  Min = 0,     Max = 200,  DefaultValue = 100 },
        ["dyn"]  = new UstxExpression { Name = "dynamics (curve)",        Abbr = "dyn",  Min = -240,  Max = 120,  DefaultValue = 0   },
        ["pitd"] = new UstxExpression { Name = "pitch deviation (curve)", Abbr = "pitd", Min = -1200, Max = 1200, DefaultValue = 0   }
    };
}
