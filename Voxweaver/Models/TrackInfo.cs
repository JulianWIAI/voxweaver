namespace Voxweaver.Models;

public class TrackInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public int NoteCount { get; set; }

    public override string ToString() => $"{Name} ({NoteCount} notes)";
}
