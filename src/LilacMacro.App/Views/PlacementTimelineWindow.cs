using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LilacMacro.App.Views;

public partial class PlacementTimelineWindow : Window
{
    private readonly PlacementTimelinePanel _timeline;

    public PlacementTimelineWindow(PlacementTimelinePanel timeline)
    {
        _timeline = timeline;
        InitializeComponent();
        TimelineHost.Content = timeline;
    }

    public PlacementTimelinePanel DetachTimeline()
    {
        TimelineHost.Content = null;
        return _timeline;
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left || IsInsideButton(eventArgs.OriginalSource)) return;
        if (eventArgs.ClickCount == 2) ToggleMaximize();
        else DragMove();
    }

    private static bool IsInsideButton(object source)
    {
        DependencyObject? current = source as DependencyObject;
        while (current is not null)
        {
            if (current is Button) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized
        ? WindowState.Normal
        : WindowState.Maximized;

    private void Minimize_OnClick(object sender, RoutedEventArgs eventArgs) => WindowState = WindowState.Minimized;

    private void Maximize_OnClick(object sender, RoutedEventArgs eventArgs) => ToggleMaximize();

    private void Close_OnClick(object sender, RoutedEventArgs eventArgs) => Close();
}
