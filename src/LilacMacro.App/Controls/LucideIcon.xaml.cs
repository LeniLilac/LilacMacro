using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace LilacMacro.App.Controls;

public partial class LucideIcon : UserControl
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data),
        typeof(Geometry),
        typeof(LucideIcon),
        new PropertyMetadata(Geometry.Empty));

    public LucideIcon()
    {
        InitializeComponent();
        Loaded += LucideIcon_OnLoaded;
    }

    public Geometry Data
    {
        get => (Geometry)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    private void LucideIcon_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        ValueSource source = DependencyPropertyHelper.GetValueSource(this, ForegroundProperty);
        if (source.BaseValueSource is not (BaseValueSource.Default or BaseValueSource.Inherited)) return;
        if (FindAncestorButton() is not null) return;
        SetResourceReference(ForegroundProperty, "InkBrush");
    }

    private ButtonBase? FindAncestorButton()
    {
        DependencyObject? current = VisualTreeHelper.GetParent(this);
        while (current is not null)
        {
            if (current is ButtonBase button) return button;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
