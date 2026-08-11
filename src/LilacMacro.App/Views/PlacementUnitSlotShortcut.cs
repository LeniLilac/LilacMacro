using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace LilacMacro.App.Views;

internal static class PlacementUnitSlotShortcut
{
    public static int? Resolve(Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.None) return null;
        return key switch
        {
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            Key.D4 or Key.NumPad4 => 4,
            Key.D5 or Key.NumPad5 => 5,
            Key.D6 or Key.NumPad6 => 6,
            _ => null,
        };
    }

    public static bool IsBlockedByFocus(DependencyObject? focusedElement)
    {
        DependencyObject? current = focusedElement;
        while (current is not null)
        {
            if (current is TextBoxBase or PasswordBox or ComboBox) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }
}
