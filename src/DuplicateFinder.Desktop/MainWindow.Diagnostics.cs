using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Automation;
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
    private Action? _refreshDiagnosticsView;
    private DiagnosticRecordingState? _lastDiagnosticsState;
    private bool _diagnosticsHeaderInitialized;

    private static void ApplyDiagnosticsTone(StyledElement element, string tone)
    {
        element.Classes.Set("recording", tone == "recording");
        element.Classes.Set("attention", tone == "attention");
    }

    private void UpdateDiagnosticsButton()
    {
        var state = _diagnostics?.State;
        if (!_diagnosticsHeaderInitialized || _lastDiagnosticsState != state)
        {
            var presentation = DiagnosticsStatusPresentation.From(state);
            DiagnosticsLabel.Text = presentation.Header;
            AutomationProperties.SetName(DiagnosticsButton, "Local diagnostics; " + presentation.Header);
            ApplyDiagnosticsTone(DiagnosticsIndicator, presentation.Tone);
            _lastDiagnosticsState = state;
            _diagnosticsHeaderInitialized = true;
        }
        _refreshDiagnosticsView?.Invoke();
        if (state != DiagnosticRecordingState.Recording) _diagnosticsTimer.Stop();
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
            var status = new TextBlock { FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            var indicator = new Ellipse();
            indicator.Classes.Add("diagnosticsIndicator");
            var statusHeading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            statusHeading.Children.Add(indicator);
            Grid.SetColumn(status, 1);
            statusHeading.Children.Add(status);
            var statusCard = new Border
            {
                Child = new StackPanel
                {
                    Spacing = 8,
                    Children = { statusHeading, new TextBlock { Text = "Record technical information locally to help investigate application issues.", TextWrapping = TextWrapping.Wrap } }
                }
            };
            statusCard.Classes.Add("reviewSection");
            statusCard.Classes.Add("diagnosticsStatus");
            var limit = new ComboBox { ItemsSource = new[] { "16 KiB", "64 KiB" }, SelectedIndex = 0, Width = 160 };
            AutomationProperties.SetName(limit, "Maximum diagnostic file size for the next recording");
            AutomationProperties.SetAutomationId(limit, "DiagnosticsSizeLimitBox");
            var start = new Button { Content = "Choose File & Start Recording", HorizontalAlignment = HorizontalAlignment.Left };
            start.Classes.Add("primary");
            var stop = new Button { Content = "Stop Recording", HorizontalAlignment = HorizontalAlignment.Left };
            var close = new Button { Content = "Close", IsDefault = true, IsCancel = true };
            var feedback = new TextBlock { IsVisible = false, TextWrapping = TextWrapping.Wrap };
            feedback.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("WorkspaceWarning"));
            AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
            Avalonia.Automation.AutomationProperties.SetAutomationId(start, "StartDiagnosticsButton");
            Avalonia.Automation.AutomationProperties.SetAutomationId(stop, "StopDiagnosticsButton");
            Avalonia.Automation.AutomationProperties.SetAutomationId(status, "DiagnosticsStatusText");
            AutomationProperties.SetAutomationId(feedback, "DiagnosticsFeedbackText");
            var privacy = new Expander
            {
                Header = "What information is recorded?", HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = new TextBlock
                {
                    Text = "Records contain a schema version, UTC timestamps, fixed operation and outcome names, optional counts and broad error categories. Scan file counts refer to files in duplicate groups.\n\nRecords exclude filenames, scanned paths, contents, exception messages and stack traces. The file picker can show your chosen location; that location is not written into diagnostic records.",
                    TextWrapping = TextWrapping.Wrap
                }
            };
            AutomationProperties.SetAutomationId(privacy, "DiagnosticsPrivacyExpander");
            AutomationProperties.SetName(privacy, "What information is recorded?");
            var retention = new Expander
            {
                Header = "Retention and recording limits", HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = new TextBlock
                {
                    Text = "Keep or delete saved files yourself. Nothing is automatically uploaded, rotated, expired or deleted.\n\nEach new recording requires a new local file; existing files are never overwritten. The selected 16 KiB or 64 KiB cap applies to that file. The queue holds at most 64 pending records. Recording stops at a file/queue limit or write failure.\n\nStop Recording closes the file after accepted records drain. Normal app close also drains accepted records. Stop before moving or sharing a file, and review it before sharing. Crashes or storage failures can leave an incomplete final record.",
                    TextWrapping = TextWrapping.Wrap
                }
            };
            AutomationProperties.SetAutomationId(retention, "DiagnosticsRetentionExpander");
            AutomationProperties.SetName(retention, "Retention and recording limits");
            var content = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    statusCard,
                    new StackPanel
                    {
                        Spacing = 8,
                        Children = { new TextBlock { Text = "Diagnostics are stored locally. Nothing is automatically uploaded.", TextWrapping = TextWrapping.Wrap }, privacy }
                    },
                    ReviewSection("Recording Settings", new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = "Maximum file size for the next recording", TextWrapping = TextWrapping.Wrap }, limit,
                            new TextBlock { Text = "Off by default on every launch. Choose a new local file to record for this app session only.", TextWrapping = TextWrapping.Wrap }, feedback
                        }
                    }),
                    new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = "Privacy and retention", FontWeight = FontWeight.SemiBold },
                            new TextBlock { Text = "Diagnostic files remain on your device until you delete them.", TextWrapping = TextWrapping.Wrap }, retention
                        }
                    }
                }
            };
            close.HorizontalAlignment = HorizontalAlignment.Right;
            var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            actions.Children.Add(start);
            actions.Children.Add(stop);
            Grid.SetColumn(close, 1);
            actions.Children.Add(close);
            var dialog = CreateReviewDialog("Local diagnostics", content, actions, close);
            dialog.MinWidth = 420;
            dialog.MinHeight = 280;
            if (Screens.ScreenFromWindow(this) is { } screen)
            {
                dialog.MaxWidth = Math.Max(dialog.MinWidth, screen.WorkingArea.Width / screen.Scaling - 32);
                dialog.MaxHeight = Math.Max(dialog.MinHeight, screen.WorkingArea.Height / screen.Scaling - 48);
                dialog.Width = Math.Min(700, dialog.MaxWidth);
                dialog.Height = Math.Min(620, dialog.MaxHeight);
            }
            var changingRecording = false;
            void RefreshPresentation()
            {
                var state = _diagnostics?.State;
                var presentation = DiagnosticsStatusPresentation.From(state);
                status.Text = presentation.Title;
                ApplyDiagnosticsTone(indicator, presentation.Tone);
                ApplyDiagnosticsTone(statusCard, presentation.Tone);
                var recording = state == DiagnosticRecordingState.Recording;
                var stopHadFocus = stop.IsFocused;
                start.IsVisible = !recording;
                stop.IsVisible = recording;
                start.IsEnabled = !changingRecording && !recording;
                stop.IsEnabled = !changingRecording && recording;
                close.IsEnabled = !changingRecording;
                if (stopHadFocus && !recording && !changingRecording) start.Focus();
            }
            _refreshDiagnosticsView = RefreshPresentation;
            RefreshPresentation();
            dialog.Closing += (_, args) => { if (changingRecording) args.Cancel = true; };
            start.Click += async (_, _) =>
            {
                changingRecording = true;
                feedback.IsVisible = false;
                RefreshPresentation();
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
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    feedback.Text = "Could not start recording. Choose a new filename in an existing local folder. Existing files are never overwritten.";
                    feedback.IsVisible = true;
                }
                finally
                {
                    changingRecording = false;
                    UpdateDiagnosticsButton();
                    if (_diagnostics?.State == DiagnosticRecordingState.Recording) stop.Focus(); else start.Focus();
                }
            };
            stop.Click += async (_, _) =>
            {
                changingRecording = true;
                RefreshPresentation();
                if (_diagnostics is not null) await _diagnostics.StopAsync();
                changingRecording = false;
                UpdateDiagnosticsButton();
                start.Focus();
            };
            close.Click += (_, _) => dialog.Close();
            await dialog.ShowDialog(this);
        }
        finally { _refreshDiagnosticsView = null; _setupDialogOpen = false; SetScanInProgress(false); DiagnosticsButton.Focus(); }
    }
}
