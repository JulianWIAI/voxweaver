using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Voxweaver.Services;
using Voxweaver.ViewModels;

namespace Voxweaver.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDropHandler(this, OnDrop);
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDragLeaveHandler(this, OnDragLeave);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null)
            _vm.ClipboardCopyRequested -= OnClipboardCopyRequested;

        _vm = DataContext as MainViewModel;

        if (_vm is not null)
            _vm.ClipboardCopyRequested += OnClipboardCopyRequested;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        DialogHelper.TopLevel = GetTopLevel(this);
    }

    // ── Drag-and-drop ────────────────────────────────────────────────────────

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool hasFiles = e.DataTransfer.Formats.Contains(DataFormat.File);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        if (_vm is not null) _vm.IsDragOver = hasFiles;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (_vm is not null) _vm.IsDragOver = false;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (_vm is not null) _vm.IsDragOver = false;

        var storageItem = e.DataTransfer.Items
            .Where(item => item.Formats.Contains(DataFormat.File))
            .Select(item => item.TryGetRaw(DataFormat.File) as IStorageItem)
            .OfType<IStorageItem>()
            .FirstOrDefault();

        if (storageItem is not null)
            _vm?.HandleFileDrop(storageItem.Path.LocalPath);

        e.Handled = true;
    }

    // ── Clipboard ────────────────────────────────────────────────────────────

    private async void OnClipboardCopyRequested(string text)
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }
}
