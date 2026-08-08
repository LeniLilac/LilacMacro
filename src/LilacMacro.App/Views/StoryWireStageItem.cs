using System.ComponentModel;
using System.Runtime.CompilerServices;
using LilacMacro.App.Debugging;

namespace LilacMacro.App.Views;

internal sealed class StoryWireStageItem(int number, StoryWireStage stage) : INotifyPropertyChanged
{
    private StoryWireStageStatus _status = StoryWireStageStatus.Waiting;
    private string _detail = string.Empty;

    public int Number { get; } = number;
    public StoryWireStage Stage { get; } = stage;
    public string Name { get; } = StoryWireTestRunner.Format(stage);
    public StoryWireStageStatus Status { get => _status; set { _status = value; Changed(); } }
    public string Detail { get => _detail; set { _detail = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
