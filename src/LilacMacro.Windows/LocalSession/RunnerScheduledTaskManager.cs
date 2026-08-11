namespace LilacMacro.Windows.LocalSession;

public sealed class RunnerScheduledTaskManager
{
    public const string TaskName = "LilacMacro Local Runner Worker";

    public void Register(string accountName, string password, string workerPath)
    {
        string qualifiedAccountName = QualifyLocalAccount(accountName, Environment.MachineName);
        Type type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Task Scheduler is unavailable.");
        dynamic service = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Task Scheduler could not be opened.");
        service.Connect();
        dynamic folder = service.GetFolder("\\");
        try { folder.DeleteTask(TaskName, 0); }
        catch (Exception error) when (IsMissingTaskFailure(error)) { }
        dynamic task = service.NewTask(0);
        task.RegistrationInfo.Description = "Starts LilacMacro's windowless worker inside the owned runner session.";
        task.Principal.UserId = qualifiedAccountName;
        task.Principal.LogonType = 1;
        task.Principal.RunLevel = 0;
        dynamic trigger = task.Triggers.Create(9);
        trigger.UserId = qualifiedAccountName;
        trigger.Enabled = true;
        dynamic action = task.Actions.Create(0);
        action.Path = workerPath;
        action.Arguments = "--serve";
        task.Settings.MultipleInstances = 2;
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        task.Settings.ExecutionTimeLimit = "PT0S";
        folder.RegisterTaskDefinition(TaskName, task, 6, qualifiedAccountName, password, 1, null);
    }

    public void Remove()
    {
        Type? type = Type.GetTypeFromProgID("Schedule.Service");
        if (type is null) return;
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        dynamic folder = service.GetFolder("\\");
        try { folder.DeleteTask(TaskName, 0); }
        catch (Exception error) when (IsMissingTaskFailure(error)) { }
    }

    public bool Exists()
    {
        Type? type = Type.GetTypeFromProgID("Schedule.Service");
        if (type is null) return false;
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        dynamic folder = service.GetFolder("\\");
        try { _ = folder.GetTask(TaskName); return true; }
        catch (Exception error) when (IsMissingTaskFailure(error)) { return false; }
    }

    internal static bool IsMissingTaskFailure(Exception error) =>
        error is FileNotFoundException ||
        error.HResult == unchecked((int)0x80070002) ||
        error.InnerException is not null && IsMissingTaskFailure(error.InnerException);

    internal static string QualifyLocalAccount(string accountName, string machineName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);
        return $"{machineName}\\{accountName}";
    }
}
