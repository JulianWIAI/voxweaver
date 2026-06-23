using CommunityToolkit.Mvvm.ComponentModel;
using Voxweaver.Models;
using Voxweaver.Services;

namespace Voxweaver.ViewModels;

public partial class PhraseViewModel : ViewModelBase
{
    private readonly G2pService _g2p;

    public Phrase Phrase { get; }
    public string Label => $"Line {Phrase.Index + 1}";
    public int NoteCount => Phrase.Notes.Count;
    public string NoteCountText => $"{NoteCount} note{(NoteCount == 1 ? "" : "s")}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyllableIndicator))]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(IsNotValid))]
    private string _lyrics = string.Empty;

    public PhraseViewModel(Phrase phrase, G2pService g2p)
    {
        Phrase = phrase;
        _g2p = g2p;
    }

    /// <summary>Live syllable count vs note count displayed next to the text box.</summary>
    public string SyllableIndicator
    {
        get
        {
            int syllables = _g2p.CountSyllablesInText(Lyrics);
            return $"{syllables}/{NoteCount} syllables";
        }
    }

    public bool IsValid => _g2p.CountSyllablesInText(Lyrics) == NoteCount;
    public bool IsNotValid => !IsValid;
}
