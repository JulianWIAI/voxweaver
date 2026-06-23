using Voxweaver.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Voxweaver.Services;

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
    public string Singer { get; set; } = "Bennett";
    public string Phonemizer { get; set; } = "OpenUtau.Core.Experimental.EnglishG2pPhonemizer";
    public UstxRendererSettings RendererSettings { get; set; } = new();
}

public class UstxRendererSettings
{
    public string Renderer { get; set; } = "WORLDLINE-R";
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
    public string Lyric { get; set; } = "a";
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
    private const int TimelineScale = 4;    // stretch all positions and durations 4x
    private const int MinDuration = 480;    // one full beat — minimum for consonant rendering

    private readonly G2pService _g2p = new();
    private readonly Random _rng = new();

    public void Export(
        string outputPath,
        string projectName,
        double bpm,
        short midiTpqn,
        IEnumerable<(Phrase phrase, string lyrics)> phraseLyricPairs)
    {
        var project = new UstxProject
        {
            Name = projectName,
            Bpm = Math.Round(bpm, 3),
            Resolution = UstxResolution,
            Expressions = BuildDefaultExpressions(),
            Tracks = new List<UstxTrack> { new UstxTrack { TrackName = "Vocal" } }
        };

        // Flatten all phrases into a single ordered list so we can look ahead
        // to the next note's position when capping duration.
        var flat = new List<(MidiNote midiNote, string syllable)>();
        foreach (var (phrase, lyrics) in phraseLyricPairs)
        {
            var syllables = _g2p.GetSyllables(lyrics);
            for (int i = 0; i < phrase.Notes.Count; i++)
                flat.Add((phrase.Notes[i], i < syllables.Count ? syllables[i] : "a"));
        }

        var ustxNotes = new List<UstxNote>();
        var velXs     = new List<long>();
        var velYs     = new List<int>();

        for (int i = 0; i < flat.Count; i++)
        {
            var (note, syllable) = flat[i];

            // Stretch position 4x so the whole timeline expands uniformly.
            long ustxPos = note.TickStart * UstxResolution / midiTpqn * TimelineScale;

            // Stretch duration 4x, then enforce the one-beat minimum.
            long scaled       = Math.Max(1, note.TickDuration * UstxResolution / midiTpqn * TimelineScale);
            long ustxDuration = Math.Max(MinDuration, scaled);

            // Cap against the next note's start position to prevent overlap.
            if (i + 1 < flat.Count)
            {
                long nextPos = flat[i + 1].midiNote.TickStart * UstxResolution / midiTpqn * TimelineScale;
                long gap = nextPos - ustxPos;
                if (gap > 0)
                    ustxDuration = Math.Min(ustxDuration, gap - 1);
            }
            ustxDuration = Math.Max(1, ustxDuration);

            ustxNotes.Add(new UstxNote
            {
                Position = ustxPos,
                Duration = ustxDuration,
                Tone     = note.Pitch,
                Lyric    = string.IsNullOrWhiteSpace(syllable) ? "a" : syllable,
                // Two anchor points — minimum OpenUtau requires in pitch.data.
                Pitch = new UstxPitch
                {
                    Data = new List<UstxPitchPoint>
                    {
                        new() { X = -40f, Y = 0f, Shape = "l" },
                        new() { X = 0f,   Y = 0f, Shape = "l" }
                    },
                    SnapFirst = true
                }
            });

            // Velocity: phrase-arch contour (quiet at edges, louder in middle) + jitter.
            velXs.Add(ustxPos);
            velYs.Add(HumanizedVelocity(i, flat.Count));
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
            .Build();

        File.WriteAllText(outputPath, serializer.Serialize(project));
    }

    // Phrase-arch: sin curve gives ~90 at the first and last notes, ~110 at the midpoint.
    // A ±5 jitter on top prevents mechanical regularity.
    private int HumanizedVelocity(int index, int total)
    {
        double t       = total > 1 ? (double)index / (total - 1) : 0.5;
        double arch    = Math.Sin(t * Math.PI);           // 0 → 1 → 0
        double contour = 90.0 + arch * 20.0;              // 90–110
        double jitter  = (_rng.NextDouble() - 0.5) * 10; // ±5
        return Math.Clamp((int)Math.Round(contour + jitter), 85, 115);
    }

    private static Dictionary<string, UstxExpression> BuildDefaultExpressions() => new()
    {
        ["vel"]  = new UstxExpression { Name = "velocity (curve)",       Abbr = "vel",  Min = 0,     Max = 200,  DefaultValue = 100 },
        ["vol"]  = new UstxExpression { Name = "volume (curve)",          Abbr = "vol",  Min = 0,     Max = 200,  DefaultValue = 100 },
        ["dyn"]  = new UstxExpression { Name = "dynamics (curve)",        Abbr = "dyn",  Min = -240,  Max = 120,  DefaultValue = 0 },
        ["pitd"] = new UstxExpression { Name = "pitch deviation (curve)", Abbr = "pitd", Min = -1200, Max = 1200, DefaultValue = 0 }
    };
}
