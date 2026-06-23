namespace Voxweaver.Models;

public class ParsedMidi
{
    public string FilePath { get; set; } = string.Empty;
    public List<TrackInfo> Tracks { get; set; } = new();
    public double Bpm { get; set; } = 120.0;
    public short TicksPerQuarterNote { get; set; } = 480;
}
