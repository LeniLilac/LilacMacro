using System.Collections.ObjectModel;
using LilacMacro.App.Views;

namespace LilacMacro.App.Runtime;

internal sealed class MacroOwnerState
{
    public ObservableCollection<PlanPrototype> Plans { get; } = PlanPrototypeFactory.CreatePlans();

    public MacroKeyBindings KeyBindings { get; } = new();

    public string PrivateServerLink { get; set; } = string.Empty;
}
