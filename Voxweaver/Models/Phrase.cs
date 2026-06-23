namespace Voxweaver.Models;

public class Phrase
{
    public int Index { get; set; }
    public List<MidiNote> Notes { get; set; } = new();
}
