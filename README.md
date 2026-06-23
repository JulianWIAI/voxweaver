# Voxweaver

A desktop application that bridges MIDI files and [OpenUtau](https://github.com/stakira/OpenUtau) vocal synthesis. Drop a MIDI file, map lyrics to phrases phrase-by-phrase, and export a ready-to-use `.ustx` project file.

---

## Features

- **Drag-and-drop MIDI ingestion** — supports `.mid` and `.midi` files
- **Auto-phrasing** — groups consecutive notes into lyric lines; a gap longer than one eighth note starts a new phrase
- **Multi-track support** — presents a selector when more than one note-bearing track is found; detects and warns about chords
- **Live syllable validation** — real-time `3/5 syllables` indicator next to each phrase text box
- **AI lyric pipeline** — exports a JSON payload (theme + syllable counts) for any LLM, ingests the response, validates syllable counts, and flags mismatches for manual review
- **OpenUtau `.ustx` export** — valid YAML with correct pitch anchors, vibrato defaults, and per-note lyric mapping
- **Navigation** — change file or change track at any point without restarting

---

## Tech Stack

| Layer | Library |
|---|---|
| UI framework | [Avalonia UI 12](https://avaloniaui.net) (MVVM, cross-platform) |
| MVVM helpers | [CommunityToolkit.Mvvm 8.4](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) |
| MIDI parsing | [Melanchall.DryWetMidi 8](https://melanchall.github.io/drywetmidi/) |
| YAML serialisation | [YamlDotNet 15](https://github.com/aaubry/YamlDotNet) |
| Runtime | .NET 8 |

---

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Run

```bash
git clone https://github.com/JulianWIAI/voxweaver.git
cd voxweaver/Voxweaver
dotnet run
```

### Build a standalone executable

```bash
# macOS (Apple Silicon)
dotnet publish -c Release -r osx-arm64 --self-contained

# macOS (Intel)
dotnet publish -c Release -r osx-x64 --self-contained

# Windows
dotnet publish -c Release -r win-x64 --self-contained
```

Output is placed in `bin/Release/net8.0/<rid>/publish/`.

---

## Workflow

### 1 — Load a MIDI file

Drop a `.mid` file onto the window or click **Browse**. Voxweaver scans all tracks and either auto-selects the only note-bearing track or presents a dropdown for you to choose the vocal melody line.

### 2 — Map lyrics

Each auto-detected phrase shows:

```
Line 1   5 notes   [________________________]   0/5 syllables
Line 2   8 notes   [________________________]   0/8 syllables
```

Type or paste lyrics into each box. The syllable count updates in real time and turns green when it matches the note count.

### 3 — AI lyric generation (optional)

1. Enter a **theme** in the AI panel (e.g. *"Geopolitical operations, covert actions in Tokyo"*)
2. Click **Export Payload (JSON)** — saves a file like:
   ```json
   {
     "theme": "Geopolitical operations, covert actions in Tokyo",
     "sequences": [
       { "lineId": "Line_01", "syllableCount": 5 },
       { "lineId": "Line_02", "syllableCount": 8 }
     ]
   }
   ```
3. Paste the payload into any LLM (ChatGPT, Claude, etc.) and ask it to return:
   ```json
   {
     "Line_01": "Sha-dows on the wall",
     "Line_02": "Watch-ing ev-ery move you make to-night"
   }
   ```
   Use hyphens to mark syllable boundaries within words.
4. Save the response as a `.json` file and click **Import Response (JSON)**. Mismatched lines are flagged in the red validation log without blocking the rest.

### 4 — Export

Click **Export to OpenUtau (.ustx)** in the status bar, choose a save location, and open the file in OpenUtau.

---

## Project Structure

```
Voxweaver/
├── Models/
│   ├── AiPayload.cs          # JSON export/import data models
│   ├── MidiNote.cs           # Pitch + tick timing
│   ├── Phrase.cs             # Group of consecutive notes
│   ├── ParsedMidi.cs         # File metadata (BPM, TPQN, tracks)
│   └── TrackInfo.cs          # Track name + note count
├── Services/
│   ├── AiPipelineService.cs  # JSON payload builder + LLM response ingester
│   ├── DialogHelper.cs       # Static file-picker bridge (TopLevel)
│   ├── G2pService.cs         # Syllable counter + consonant-cluster splitter
│   ├── MidiParserService.cs  # DryWetMidi parsing + auto-phrasing
│   └── UstxExportService.cs  # YAML .ustx serialisation
├── ViewModels/
│   ├── MainViewModel.cs      # App state, commands, navigation
│   └── PhraseViewModel.cs    # Per-phrase lyrics + live validation
└── Views/
    ├── MainWindow.axaml      # UI layout
    └── MainWindow.axaml.cs   # Drag-drop, clipboard wiring
```

---

## License

MIT
