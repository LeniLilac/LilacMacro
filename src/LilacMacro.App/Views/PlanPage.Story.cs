using LilacMacro.App.Runtime;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Views;

public partial class PlanPage
{
    private static readonly string[] StoryMaps =
    [
        "School Grounds",
        "Flower Forest",
        "Rose Kingdom",
        "Fairy King Forest",
        "King's Tomb",
        "East Town",
    ];

    private static readonly string[] StoryActs =
    [
        "Act 1",
        "Act 2",
        "Act 3",
        "Act 4",
        "Act 5",
        "Infinite",
        "Mastery",
    ];

    private bool IsInfiniteStory =>
        _editorMode == PlanTaskMode.Story &&
        string.Equals(TaskStoryActCombo.SelectedItem as string, "Infinite", StringComparison.Ordinal);

    private bool IsMasteryStory =>
        _editorMode == PlanTaskMode.Story &&
        string.Equals(TaskStoryActCombo.SelectedItem as string, "Mastery", StringComparison.Ordinal);

    private string SelectedEditorRoute()
    {
        if (_editorMode != PlanTaskMode.Story)
            return TaskRouteCombo.SelectedItem as string ?? DefaultRoute(_editorMode);
        string map = TaskStoryMapCombo.SelectedItem as string ?? StoryMaps[0];
        string act = TaskStoryActCombo.SelectedItem as string ?? StoryActs[0];
        return $"{map} · {act}";
    }

    private void SetStoryRoute(string route)
    {
        try
        {
            (string map, StoryAct act) = MacroTaskOptionsFactory.ParseRoute(route);
            TaskStoryMapCombo.SelectedItem = StoryMaps.FirstOrDefault(
                candidate => string.Equals(candidate, map, StringComparison.OrdinalIgnoreCase)) ?? StoryMaps[0];
            TaskStoryActCombo.SelectedItem = ActLabel(act);
        }
        catch (InvalidDataException)
        {
            TaskStoryMapCombo.SelectedIndex = 0;
            TaskStoryActCombo.SelectedIndex = 0;
        }
        UpdateStoryOptions();
    }

    private void UpdateStoryOptions()
    {
        if (TaskInfiniteWavePanel is null || TaskHardModeCheck is null) return;
        TaskInfiniteWavePanel.Visibility = IsInfiniteStory ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        TaskHardModeCheck.IsEnabled = _editorMode == PlanTaskMode.Story && !IsInfiniteStory && !IsMasteryStory;
        if (!TaskHardModeCheck.IsEnabled) TaskHardModeCheck.IsChecked = false;
        if (TaskTargetLabel is not null && _editorMode == PlanTaskMode.Story)
            TaskTargetLabel.Text = IsInfiniteStory ? "RUNS" : "VICTORIES";
    }

    private static string ActLabel(StoryAct act) => act switch
    {
        StoryAct.Act1 => "Act 1",
        StoryAct.Act2 => "Act 2",
        StoryAct.Act3 => "Act 3",
        StoryAct.Act4 => "Act 4",
        StoryAct.Act5 => "Act 5",
        StoryAct.Infinite => "Infinite",
        StoryAct.Mastery => "Mastery",
        _ => throw new ArgumentOutOfRangeException(nameof(act)),
    };
}
