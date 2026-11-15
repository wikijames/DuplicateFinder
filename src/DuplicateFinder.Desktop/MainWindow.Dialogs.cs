using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DuplicateFinder.Desktop;

public partial class MainWindow
{
    private static Border ReviewSection(string title, Control body)
    {
        var section = new Border
        {
            Child = new StackPanel
            {
                Spacing = 8,
                Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold }, body }
            }
        };
        section.Classes.Add("reviewSection");
        return section;
    }

    private Window CreateReviewDialog(string title, Control body, Control actions, Button initialFocus)
    {
        var content = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 16 };
        content.Children.Add(new ScrollViewer { Content = body });
        Grid.SetRow(actions, 1);
        content.Children.Add(actions);
        var dialog = new Window
        {
            Title = title, Width = 700, Height = 560, MinWidth = 480, MinHeight = 360,
            Content = content, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var returnFocus = FocusManager?.GetFocusedElement() as Control;
        dialog.Opened += (_, _) => initialFocus.Focus();
        dialog.Closed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (returnFocus is { IsEnabled: true, IsVisible: true }) returnFocus.Focus();
        }, Avalonia.Threading.DispatcherPriority.Background);
        return dialog;
    }
}
