using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Voxweaver.Services;

/// <summary>
/// Thin static bridge so ViewModels can request file dialogs without taking
/// a hard dependency on any View type.  Set TopLevel once in MainWindow.OnLoaded.
/// </summary>
public static class DialogHelper
{
    public static TopLevel? TopLevel { get; set; }

    public static async Task<string?> OpenMidiFileAsync()
    {
        if (TopLevel?.StorageProvider is not { } sp) return null;

        var results = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open MIDI File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("MIDI Files") { Patterns = new[] { "*.mid", "*.midi" } },
                FilePickerFileTypes.All
            }
        });

        return results.FirstOrDefault()?.Path.LocalPath;
    }

    public static async Task<string?> SaveUstxFileAsync(string suggestedName)
    {
        if (TopLevel?.StorageProvider is not { } sp) return null;

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save OpenUtau Project",
            SuggestedFileName = suggestedName,
            DefaultExtension = "ustx",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("OpenUtau Project") { Patterns = new[] { "*.ustx" } }
            }
        });

        return file?.Path.LocalPath;
    }

    public static async Task<string?> SaveJsonFileAsync(string suggestedName)
    {
        if (TopLevel?.StorageProvider is not { } sp) return null;

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save AI Payload",
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSON File") { Patterns = new[] { "*.json" } }
            }
        });

        return file?.Path.LocalPath;
    }

    public static async Task<string?> OpenJsonFileAsync()
    {
        if (TopLevel?.StorageProvider is not { } sp) return null;

        var results = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open LLM Response JSON",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("JSON File") { Patterns = new[] { "*.json" } },
                FilePickerFileTypes.All
            }
        });

        return results.FirstOrDefault()?.Path.LocalPath;
    }
}
