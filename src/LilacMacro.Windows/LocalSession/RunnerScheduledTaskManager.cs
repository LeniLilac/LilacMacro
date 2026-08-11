namespace LilacMacro.Windows.LocalSession;

public sealed class RunnerScheduledTaskManager
{
    public const string TaskName = "LilacMacro Local Runner Worker";

    public void Register(string accountName, string password, string workerPath)
    {
        Type type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Task Scheduler is unavailable.");
        dynamic service = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Task Scheduler could not be opened.");
        service.Connect();
        dynamic folder = service.GetFolder("\\");
        try { folder.DeleteTask(TaskName, 0); } catch (System.Runtime.InteropServices.COMException) { }
        dynamic task = service.NewTask(0);
        task.RegistrationInfo.Description = "Starts LilacMacro's windowless worker inside the owned runner session.";
        task.Principal.UserId = $".\\{accountName}";
        task.Principal.LogonType = 1;
        task.Principal.RunLevel = 0;
        dynamic trigger = task.Triggers.Create(9);
        trigger.UserId = $".\\{accountName}";
        trigger.Enabled = true;
        dynamic action = task.Actions.Create(0);
        action.Path = workerPath;
        action.Arguments = "--serve";
        task.Settings.MultipleInstances = 2;
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        task.Settings.ExecutionTimeLimit = "PT0S";
        folder.RegisterTaskDefinition(TaskName, task, 6, $".\\{accountName}", password, 1, null);
    }

    public void Remove()
    {
        Type? type = Type.GetTypeFromProgID("Schedule.Service");
        if (type is null) return;
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        dynamic folder = service.GetFolder("\\");
        try { folder.DeleteTask(TaskName, 0); } catch (System.Runtime.InteropServices.COMException) { }
    }

    public bool Exists()
    {
        Type? type = Type.GetTypeFromProgID("Schedule.Service");
        if (type is null) return false;
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        dynamic folder = service.GetFolder("\\");
        try { _ = folder.GetTask(TaskName); return true; }
        catch (System.Runtime.InteropServices.COMException) { return false; }
    }
}
