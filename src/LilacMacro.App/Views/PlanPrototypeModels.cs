using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Views;

public enum PlanTaskMode
{
    Challenge,
    Expedition,
    Story,
    Raid,
    Event,
    Utilities,
}

public abstract class PlanBlockPrototype : INotifyPropertyChanged
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public abstract PlanBlockPrototype Clone();
}

public sealed class PlanTaskPrototype : PlanBlockPrototype
{
    private int _priority;
    private PlanTaskMode _mode;
    private string _route = "School Grounds · Act 1";
    private int _target = 15;
    private int _defeatRetries;
    private int _difficulty = 1;
    private int _bossesBeforeExtract = 1;
    private bool _extractAtCheckpoint = true;
    private string _rewardTarget = "None";
    private bool _hardMode;
    private bool _runTrait = true;
    private bool _runStat = true;
    private bool _runSprite = true;
    private IReadOnlyList<string> _shopItemIds = [];

    public int Priority { get => _priority; set => Set(ref _priority, value); }
    public PlanTaskMode Mode { get => _mode; set { if (Set(ref _mode, value)) NotifySummary(); } }
    public string Route { get => _route; set { if (Set(ref _route, value)) NotifySummary(); } }
    public int Target { get => _target; set { if (Set(ref _target, Math.Max(1, value))) NotifySummary(); } }
    public int DefeatRetries { get => _defeatRetries; set => Set(ref _defeatRetries, Math.Clamp(value, 0, 20)); }
    public int Difficulty { get => _difficulty; set { if (Set(ref _difficulty, Math.Clamp(value, 1, 3))) NotifySummary(); } }
    public int BossesBeforeExtract { get => _bossesBeforeExtract; set => Set(ref _bossesBeforeExtract, Math.Clamp(value, 0, 99)); }
    public bool ExtractAtCheckpoint { get => _extractAtCheckpoint; set => Set(ref _extractAtCheckpoint, value); }
    public string RewardTarget { get => _rewardTarget; set => Set(ref _rewardTarget, value); }
    public bool HardMode { get => _hardMode; set { if (Set(ref _hardMode, value)) NotifySummary(); } }
    public bool RunTrait { get => _runTrait; set { if (Set(ref _runTrait, value)) NotifySummary(); } }
    public bool RunStat { get => _runStat; set { if (Set(ref _runStat, value)) NotifySummary(); } }
    public bool RunSprite { get => _runSprite; set { if (Set(ref _runSprite, value)) NotifySummary(); } }
    public IReadOnlyList<string> ShopItemIds
    {
        get => _shopItemIds;
        set => Set(ref _shopItemIds, value?.Distinct(StringComparer.Ordinal).ToArray() ?? []);
    }

    public string ModeLabel => Mode switch
    {
        PlanTaskMode.Utilities => "Utilities",
        _ => Mode.ToString(),
    };

    public string Name => Mode switch
    {
        PlanTaskMode.Challenge => "Challenge rotation",
        PlanTaskMode.Utilities => Route,
        PlanTaskMode.Expedition => $"Expedition · {Route}",
        PlanTaskMode.Story => $"Story · {Route}{(HardMode ? " · Hard" : string.Empty)}",
        PlanTaskMode.Raid => $"Raid · {Route}",
        PlanTaskMode.Event => $"Event · {Route}",
        _ => Route,
    };

    public string TargetLabel => Mode switch
    {
        PlanTaskMode.Utilities => UtilityTaskPolicy.ScheduleLabel(Route, Target),
        PlanTaskMode.Challenge => "Every reset",
        _ => $"{Target} victories",
    };

    public string Status => Mode == PlanTaskMode.Utilities ? "Ready" : "0W / 0L";

    public override PlanTaskPrototype Clone() => new()
    {
        Priority = Priority,
        Mode = Mode,
        Route = Route,
        Target = Target,
        DefeatRetries = DefeatRetries,
        Difficulty = Difficulty,
        BossesBeforeExtract = BossesBeforeExtract,
        ExtractAtCheckpoint = ExtractAtCheckpoint,
        RewardTarget = RewardTarget,
        HardMode = HardMode,
        RunTrait = RunTrait,
        RunStat = RunStat,
        RunSprite = RunSprite,
        ShopItemIds = ShopItemIds.ToArray(),
    };

    private void NotifySummary()
    {
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(TargetLabel));
    }
}

public sealed class PlanLoopPrototype : PlanBlockPrototype
{
    private string _label = "Loop 1";
    private bool _forever = true;
    private int _repeatCount = 2;

    public string Label { get => _label; set => Set(ref _label, value); }
    public bool Forever { get => _forever; set { if (Set(ref _forever, value)) OnPropertyChanged(nameof(Status)); } }
    public int RepeatCount { get => _repeatCount; set { if (Set(ref _repeatCount, Math.Clamp(value, 1, 100000))) OnPropertyChanged(nameof(Status)); } }
    public ObservableCollection<PlanBlockPrototype> Children { get; } = [];
    public string Status => Forever ? "Forever · 0 runs completed." : $"{RepeatCount} runs · 0 runs completed.";

    public override PlanLoopPrototype Clone()
    {
        PlanLoopPrototype copy = new() { Label = Label, Forever = Forever, RepeatCount = RepeatCount };
        foreach (PlanBlockPrototype child in Children) copy.Children.Add(child.Clone());
        return copy;
    }
}

public sealed class PlanPrototype
{
    public PlanPrototype(string name, IEnumerable<PlanBlockPrototype> blocks)
    {
        Name = name;
        Blocks = new ObservableCollection<PlanBlockPrototype>(blocks);
    }

    public string Name { get; set; }
    public ObservableCollection<PlanBlockPrototype> Blocks { get; }

    public PlanPrototype Clone(string name) => new(name, Blocks.Select(block => block.Clone()));
}

public sealed record PlanDestinationChoice(string Name, PlanLoopPrototype? Loop);

public static class PlanPrototypeFactory
{
    public static ObservableCollection<PlanPrototype> CreatePlans() =>
    [
        new("Daily rotation",
        [
            Task(PlanTaskMode.Utilities, "Gold Mine refuel", 400),
            Task(PlanTaskMode.Utilities, "Resource Drill refuel", 400),
            Loop("Loop 1",
            [
                Task(PlanTaskMode.Event, "Villain Invasion · Act 1", 150),
                Task(PlanTaskMode.Expedition, "School Grounds", 15),
            ]),
        ]),
        new("Weekend push",
        [
            Task(PlanTaskMode.Story, "King's Tomb · Mastery", 25),
            Task(PlanTaskMode.Raid, "Spirit City · Act 3", 20),
        ]),
    ];

    private static PlanTaskPrototype Task(PlanTaskMode mode, string route, int target) => new()
    {
        Mode = mode,
        Route = route,
        Target = target,
    };

    private static PlanLoopPrototype Loop(string label, IEnumerable<PlanTaskPrototype> tasks)
    {
        PlanLoopPrototype loop = new() { Label = label };
        foreach (PlanTaskPrototype task in tasks) loop.Children.Add(task);
        return loop;
    }
}
