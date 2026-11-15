using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace DuplicateFinder.Desktop;

public partial class MainWindow
{
    private SessionDiagnostics? _diagnostics;
    private bool _diagnosticsStopping;
    private bool _diagnosticsDrained;
    private readonly Avalonia.Threading.DispatcherTimer _diagnosticsTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private void UpdateDiagnosticsButton()
    {
        DiagnosticsButton.Content = _diagnostics?.State switch
        {
            DiagnosticRecordingState.Recording => "Diagnostics: on",
            DiagnosticRecordingState.WriteFailed => "Diagnostics: failed",
            DiagnosticRecordingState.SizeLimitReached or DiagnosticRecordingState.QueueLimitReached => "Diagnostics: limit",
            _ => "Diagnostics: off"
        };
        if (_diagnostics?.State != DiagnosticRecordingState.Recording) _diagnosticsTimer.Stop();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_diagnostics is not null && !_diagnosticsDrained)
        {
            e.Cancel = true;
            if (!_diagnosticsStopping)
            {
                _diagnosticsStopping = true;
                _scanCancellation?.Cancel();
                _previewCancellation?.Cancel();
                await _diagnostics.StopAsync();
                _diagnosticsDrained = true;
                _diagnosticsTimer.Stop();
                Avalonia.Threading.Dispatcher.UIThread.Post(Close);
            }
            return;
        }
        base.OnClosing(e);
    }

    private async void DiagnosticsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SetupIsBusy) return;
        _setupDialogOpen = true;
        SetScanInProgress(false);
        try
        {
            var status = new TextBlock { Text = "State: " + (_diagnostics?.State.ToString() ?? "Off"), TextWrapping = TextWrapping.Wrap };
            var limit = new ComboBox { ItemsSource = new[] { "16 KiB", "64 KiB" }, SelectedIndex = 0, Width = 160 };
            Avalonia.Automation.AutomationProperties.SetName(limit, "Maximum diagnostic file size");
            var start = new Button { Content = "Choose new file and start", IsEnabled = _diagnostics?.State != DiagnosticRecordingState.Recording };
            var stop = new Button { Content = "Stop and close file", IsEnabled = _diagnostics?.State == DiagnosticRecordingState.Recording };
            var close = new Button { Content = "Close", IsDefault = true, IsCancel = true };
            Avalonia.Automation.AutomationProperties.SetAutomationId(start, "StartDiagnosticsButton");
            Avalonia.Automation.AutomationProperties.SetAutomationId(stop, "StopDiagnosticsButton");
            Avalonia.Automation.AutomationProperties.SetAutomationId(status, "DiagnosticsStatusText");
            var content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 14,
                Children =
                {
                    new TextBlock { Text = "Optional local diagnostics", FontSize = 18 },
                    new TextBlock { Text = "Off by default, for this app session only. Records include UTC times, fixed operation names, counts and broad error categories. No paths, filenames, file content, exception messages or stack traces are recorded. Nothing is uploaded.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Choose a new local file. Recording stops at the size or queue limit, or after a write failure. Existing files are never overwritten. Stop closes the file after queued records are written.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Retention: saved files remain until you delete them. The app never rotates or deletes logs. Choose local storage and keep only files you need; review them before sharing.", TextWrapping = TextWrapping.Wrap },
                    status, new TextBlock { Text = "Maximum size for the next recording" }, limit
                }
            };
            content.Margin = new Thickness(0);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            var actions = new StackPanel
            {
                Spacing = 8,
                Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { start, stop } }, close }
            };
            var dialog = CreateReviewDialog("Local diagnostics", content, actions, close);
            var changingRecording = false;
            dialog.Closing += (_, args) => { if (changingRecording) args.Cancel = true; };
            start.Click += async (_, _) =>
            {
                changingRecording = true;
                start.IsEnabled = false;
                close.IsEnabled = false;
                try
                {
                    var file = await dialog.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = "Choose a new local diagnostic file", DefaultExtension = "jsonl",
                        SuggestedFileName = "duplicatefinder-diagnostics-" + Guid.NewGuid().ToString("N") + ".jsonl"
                    });
                    if (file is null) return;
                    var path = file.TryGetLocalPath() ?? throw new ArgumentException("A local file is required.");
                    if (_diagnostics is not null) await _diagnostics.StopAsync();
                    _diagnostics = await SessionDiagnostics.StartAsync(path, limit.SelectedIndex == 1 ? 65536 : 16384);
                    _diagnosticsTimer.Start();
                    status.Text = "Recording to the selected file. Stop before moving or sharing it.";
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    status.Text = "Could not start diagnostics. Choose a new filename in an existing local folder.";
                }
                finally
                {
                    changingRecording = false;
                    UpdateDiagnosticsButton();
                    start.IsEnabled = _diagnostics?.State != DiagnosticRecordingState.Recording;
                    stop.IsEnabled = _diagnostics?.State == DiagnosticRecordingState.Recording;
                    close.IsEnabled = true;
                }
            };
            stop.Click += async (_, _) =>
            {
                changingRecording = true;
                stop.IsEnabled = false;
                close.IsEnabled = false;
                if (_diagnostics is not null) await _diagnostics.StopAsync();
                UpdateDiagnosticsButton();
                status.Text = "Recording stopped. Saved files remain until you delete them.";
                start.IsEnabled = true;
                close.IsEnabled = true;
                changingRecording = false;
            };
            close.Click += (_, _) => dialog.Close();
            await dialog.ShowDialog(this);
        }
        finally { _setupDialogOpen = false; SetScanInProgress(false); DiagnosticsButton.Focus(); }
    }
}
