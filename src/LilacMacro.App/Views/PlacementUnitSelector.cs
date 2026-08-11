using System.Windows.Controls;

namespace LilacMacro.App.Views;

internal sealed class PlacementUnitSelector(params RadioButton[] buttons)
{
    private readonly RadioButton[] _buttons = buttons.Length == 6
        ? buttons
        : throw new ArgumentException("Placement unit selector requires six buttons.", nameof(buttons));

    public int SelectedSlot => _buttons
        .FirstOrDefault(button => button.IsChecked == true)?.Tag is string tag &&
        int.TryParse(tag, out int slot)
            ? slot
            : 1;

    public void Select(int slot)
    {
        RadioButton button = _buttons.FirstOrDefault(candidate =>
            candidate.Tag is string tag && int.TryParse(tag, out int value) && value == slot)
            ?? throw new ArgumentOutOfRangeException(nameof(slot));
        button.IsChecked = true;
    }
}
