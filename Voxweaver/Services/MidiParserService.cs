using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Voxweaver.Models;

namespace Voxweaver.Services;

public class MidiParserService
{
    public ParsedMidi ParseFile(string filePath)
    {
        var midiFile = MidiFile.Read(filePath);
        var result = new ParsedMidi { FilePath = filePath };

        if (midiFile.TimeDivision is TicksPerQuarterNoteTimeDivision tpq)
            result.TicksPerQuarterNote = tpq.TicksPerQuarterNote;

        result.Bpm = ExtractBpm(midiFile);

        int trackIndex = 0;
        foreach (var chunk in midiFile.Chunks.OfType<TrackChunk>())
        {
            var notes = chunk.GetNotes().ToList();
            if (notes.Count == 0) { trackIndex++; continue; }

            var trackName = chunk.Events
                .OfType<SequenceTrackNameEvent>()
                .FirstOrDefault()?.Text ?? $"Track {trackIndex}";

            result.Tracks.Add(new TrackInfo
            {
                Index = trackIndex,
                Name = trackName,
                NoteCount = notes.Count
            });

            trackIndex++;
        }

        return result;
    }

    public (List<Phrase> Phrases, bool HasChords) BuildPhrases(string filePath, int trackIndex)
    {
        var midiFile = MidiFile.Read(filePath);

        short tpqn = 480;
        if (midiFile.TimeDivision is TicksPerQuarterNoteTimeDivision tpq)
            tpqn = tpq.TicksPerQuarterNote;

        // A gap longer than an eighth note triggers a new phrase.
        long eighthNoteTicks = tpqn / 2;

        var trackChunks = midiFile.Chunks.OfType<TrackChunk>().ToList();
        if (trackIndex >= trackChunks.Count)
            return (new List<Phrase>(), false);

        var notes = trackChunks[trackIndex].GetNotes()
            .OrderBy(n => n.Time)
            .ToList();

        if (notes.Count == 0)
            return (new List<Phrase>(), false);

        // Chord detection: any note that starts before the previous one ends.
        bool hasChords = false;
        for (int i = 1; i < notes.Count; i++)
        {
            if (notes[i].Time < notes[i - 1].Time + notes[i - 1].Length)
            {
                hasChords = true;
                break;
            }
        }

        var phrases = new List<Phrase>();
        var current = new Phrase { Index = 0 };
        phrases.Add(current);

        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            current.Notes.Add(new MidiNote
            {
                Pitch = n.NoteNumber,
                TickStart = n.Time,
                TickDuration = n.Length
            });

            if (i < notes.Count - 1)
            {
                long gap = notes[i + 1].Time - (n.Time + n.Length);
                if (gap > eighthNoteTicks)
                {
                    current = new Phrase { Index = phrases.Count };
                    phrases.Add(current);
                }
            }
        }

        phrases = phrases.Where(p => p.Notes.Count > 0).ToList();
        for (int i = 0; i < phrases.Count; i++)
            phrases[i].Index = i;

        return (phrases, hasChords);
    }

    private static double ExtractBpm(MidiFile midiFile)
    {
        var tempoEvent = midiFile.Chunks
            .OfType<TrackChunk>()
            .SelectMany(c => c.Events.OfType<SetTempoEvent>())
            .FirstOrDefault();

        if (tempoEvent != null && tempoEvent.MicrosecondsPerQuarterNote > 0)
            return 60_000_000.0 / tempoEvent.MicrosecondsPerQuarterNote;

        return 120.0;
    }
}
