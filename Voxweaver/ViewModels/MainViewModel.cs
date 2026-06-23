using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Voxweaver.Models;
using Voxweaver.Services;

namespace Voxweaver.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly MidiParserService _midiParser = new();
    private readonly UstxExportService _ustxExport = new();
    private readonly G2pService _g2p = new();
    private readonly AiPipelineService _aiPipeline = new();

    private ParsedMidi? _parsedMidi;

    // ── State flags ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDropZone))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(CanChangeTrack))]
    private bool _hasFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTrackSelector))]
    private bool _needsTrackSelection;

    [ObservableProperty]
    private bool _showChordWarning;

    [ObservableProperty]
    private bool _isDragOver;

    [ObservableProperty]
    private string _statusMessage = "Drop a .mid or .midi file to get started";

    [ObservableProperty]
    private bool _isBusy;

    // ── Track selection ──────────────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<TrackInfo> _availableTracks = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmTrackCommand))]
    private TrackInfo? _selectedTrack;

    // ── Phrase list ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(CanChangeTrack))]
    [NotifyCanExecuteChangedFor(nameof(CopyAiSummaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportUstxCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportAiPayloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportAiResponseCommand))]
    private ObservableCollection<PhraseViewModel> _phrases = new();

    // ── Derived visibility ───────────────────────────────────────────────────

    public bool ShowDropZone => !HasFile;
    public bool ShowContent  => HasFile && Phrases.Count > 0;
    public bool ShowTrackSelector => NeedsTrackSelection;

    /// True when the phrase list is showing and there are multiple tracks to switch between.
    public bool CanChangeTrack => ShowContent && AvailableTracks.Count > 1;

    // ── File handling ────────────────────────────────────────────────────────

    public void HandleFileDrop(string filePath)
    {
        if (!filePath.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) &&
            !filePath.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Only .mid and .midi files are supported.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Parsing MIDI…";

            _parsedMidi = _midiParser.ParseFile(filePath);

            if (_parsedMidi.Tracks.Count == 0)
            {
                StatusMessage = "No note-bearing tracks found in this file.";
                return;
            }

            HasFile = true;

            if (_parsedMidi.Tracks.Count == 1)
            {
                LoadTrack(_parsedMidi.Tracks[0]);
            }
            else
            {
                AvailableTracks = new ObservableCollection<TrackInfo>(_parsedMidi.Tracks);
                SelectedTrack = AvailableTracks[0];
                NeedsTrackSelection = true;
                StatusMessage = "Multiple tracks found — select the vocal melody track.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error reading file: {ex.Message}";
            HasFile = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirmTrack))]
    private void ConfirmTrack()
    {
        if (SelectedTrack is null) return;
        NeedsTrackSelection = false;
        LoadTrack(SelectedTrack);
    }

    private bool CanConfirmTrack() => SelectedTrack is not null;

    private void LoadTrack(TrackInfo track)
    {
        if (_parsedMidi is null) return;

        var (phrases, hasChords) = _midiParser.BuildPhrases(_parsedMidi.FilePath, track.Index);

        ShowChordWarning = hasChords;

        Phrases = new ObservableCollection<PhraseViewModel>(
            phrases.Select(p => new PhraseViewModel(p, _g2p)));

        StatusMessage = hasChords
            ? $"Loaded {track.Name} — {phrases.Count} phrase(s). ⚠ Chords detected."
            : $"Loaded {track.Name} — {phrases.Count} phrase(s).";
    }

    [RelayCommand]
    private void DismissChordWarning() => ShowChordWarning = false;

    // ── Navigation: back buttons ─────────────────────────────────────────────

    /// Opens the file picker and loads a new MIDI, resetting all existing state.
    [RelayCommand]
    private async Task ChangeFile()
    {
        var path = await DialogHelper.OpenMidiFileAsync();
        if (path is null) return;

        _parsedMidi = null;
        HasFile = false;
        NeedsTrackSelection = false;
        ShowChordWarning = false;
        AvailableTracks.Clear();
        SelectedTrack = null;
        Phrases = new ObservableCollection<PhraseViewModel>();
        AiValidationLog = string.Empty;
        HasAiValidationIssues = false;

        HandleFileDrop(path);
    }

    /// Returns to the track-selector panel so the user can pick a different track.
    [RelayCommand]
    private void ChangeTrack()
    {
        ShowChordWarning = false;
        Phrases = new ObservableCollection<PhraseViewModel>();
        NeedsTrackSelection = true;
        StatusMessage = "Select a different vocal melody track.";
    }

    // ── Top bar: copy summary for AI ────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(HasPhrases))]
    private void CopyAiSummary()
    {
        var lines = Phrases.Select((p, i) =>
            $"Phrase {i + 1}: {p.NoteCount} note{(p.NoteCount == 1 ? "" : "s")}.");
        var summary = string.Join("\n", lines);

        ClipboardCopyRequested?.Invoke(summary);
        StatusMessage = "Summary copied to clipboard.";
    }

    private bool HasPhrases() => Phrases.Count > 0;

    // ── Bottom bar: export ───────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(HasPhrases))]
    private async Task ExportUstx()
    {
        if (_parsedMidi is null) return;

        var projectName = Path.GetFileNameWithoutExtension(_parsedMidi.FilePath);
        var savePath = await DialogHelper.SaveUstxFileAsync(projectName);
        if (savePath is null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Exporting…";

            var pairs = Phrases.Select(p => (p.Phrase, p.Lyrics));
            _ustxExport.Export(savePath, projectName, _parsedMidi.Bpm,
                               _parsedMidi.TicksPerQuarterNote, pairs);

            StatusMessage = $"Exported → {Path.GetFileName(savePath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── AI lyric pipeline ────────────────────────────────────────────────────

    [ObservableProperty]
    private string _aiTheme = string.Empty;

    [ObservableProperty]
    private string _aiValidationLog = string.Empty;

    [ObservableProperty]
    private bool _hasAiValidationIssues;

    /// <summary>
    /// Serialises all phrases into a JSON payload and saves it to disk so the
    /// user can feed it to an LLM.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasPhrases))]
    private async Task ExportAiPayload()
    {
        var savePath = await DialogHelper.SaveJsonFileAsync("ai_payload");
        if (savePath is null) return;

        try
        {
            var json = _aiPipeline.BuildExportJson(AiTheme, Phrases);
            await File.WriteAllTextAsync(savePath, json);
            StatusMessage = $"AI payload saved → {Path.GetFileName(savePath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Reads the LLM's JSON response, validates syllable counts, writes flagged
    /// lines to the log, and applies normalised lyrics to matching phrase rows.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasPhrases))]
    private async Task ImportAiResponse()
    {
        var path = await DialogHelper.OpenJsonFileAsync();
        if (path is null) return;

        try
        {
            IsBusy = true;
            var json = await File.ReadAllTextAsync(path);
            var result = _aiPipeline.IngestResponse(json, Phrases);

            // Apply all lyrics — both valid and flagged (user sees the flag in the log).
            foreach (var (lineId, normLyrics) in result.NormalisedLyrics)
            {
                if (AiPipelineService.TryParseIndex(lineId, out int idx) && idx < Phrases.Count)
                    Phrases[idx].Lyrics = normLyrics;
            }

            if (result.HasIssues)
            {
                HasAiValidationIssues = true;
                AiValidationLog = string.Join("\n", result.Issues.Select(i => $"⚠  {i}"));
                StatusMessage = $"{result.Issues.Count} syllable mismatch(es) — review the log below.";
            }
            else
            {
                HasAiValidationIssues = false;
                AiValidationLog = string.Empty;
                StatusMessage = $"AI lyrics imported — all {result.NormalisedLyrics.Count} line(s) validated.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Event raised to let MainWindow copy to clipboard ────────────────────
    public event Action<string>? ClipboardCopyRequested;
}
