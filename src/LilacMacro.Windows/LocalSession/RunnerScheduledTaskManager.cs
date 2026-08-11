using LilacMacro.Core.LocalSession;

namespace LilacMacro.Windows.LocalSession;

public sealed class RunnerScheduledTaskManager
{
    public const string LegacyTaskName = "LilacMacro Local Runner Worker";
    internal const int InteractiveTokenLogonType = 3;
    internal const int LogonTriggerType = 9;
    internal const int SessionStateChangeTriggerType = 11;
    internal const int RemoteConnectStateChange = 3;

    public void Register(LocalRunnerProfile profile, string appPath, string configurationRoot)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string qualifiedAccountName = QualifyLocalAccount(profile.AccountName, Environment.MachineName);
        dynamic service = OpenService();
        dynamic folder = service.GetFolder("\\");
        string taskName = TaskNameFor(profile.Id);
        DeleteIfPresent(folder, taskName);
        dynamic task = service.NewTask(0);
        task.RegistrationInfo.Description = $"Starts the LilacMacro UI in {profile.DisplayName}.";
        task.Principal.UserId = qualifiedAccountName;
        task.Principal.LogonType = InteractiveTokenLogonType;
        task.Principal.RunLevel = 0;
        dynamic logonTrigger = task.Triggers.Create(LogonTriggerType);
        logonTrigger.UserId = qualifiedAccountName;
        logonTrigger.Enabled = true;
        dynamic reconnectTrigger = task.Triggers.Create(SessionStateChangeTriggerType);
        reconnectTrigger.UserId = qualifiedAccountName;
        reconnectTrigger.StateChange = RemoteConnectStateChange;
        reconnectTrigger.Enabled = true;
        dynamic action = task.Actions.Create(0);
        action.Path = Path.GetFullPath(appPath);
        action.Arguments = CreateArguments(profile, configurationRoot);
        action.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(appPath));
        task.Settings.MultipleInstances = 2;
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        task.Settings.ExecutionTimeLimit = "PT0S";
        folder.RegisterTaskDefinition(taskName, task, 6, qualifiedAccountName, null, InteractiveTokenLogonType, null);
    }

    public void Remove(string profileId)
    {
        dynamic? service = TryOpenService();
        if (service is null) return;
        DeleteIfPresent(service.GetFolder("\\"), TaskNameFor(profileId));
    }

    public void RemoveLegacyWorkerTask()
    {
        dynamic? service = TryOpenService();
        if (service is null) return;
        DeleteIfPresent(service.GetFolder("\\"), LegacyTaskName);
    }

    public bool Exists(string profileId)
    {
        dynamic? service = TryOpenService();
        if (service is null) return false;
        dynamic folder = service.GetFolder("\\");
        try { _ = folder.GetTask(TaskNameFor(profileId)); return true; }
        catch (Exception error) when (IsMissingTaskFailure(error)) { return false; }
    }

    public bool LegacyWorkerTaskExists()
    {
        dynamic? service = TryOpenService();
        if (service is null) return false;
        dynamic folder = service.GetFolder("\\");
        try { _ = folder.GetTask(LegacyTaskName); return true; }
        catch (Exception error) when (IsMissingTaskFailure(error)) { return false; }
    }

    public void Run(string profileId)
    {
        dynamic service = OpenService();
        dynamic task = service.GetFolder("\\").GetTask(TaskNameFor(profileId));
        _ = task.Run(null);
    }

    internal static string TaskNameFor(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        if (profileId.Length > 32 || !profileId.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            throw new ArgumentException("Runner profile identifier is invalid.", nameof(profileId));
        return $"LilacMacro Instance {profileId}";
    }

    internal static string CreateArguments(LocalRunnerProfile profile, string configurationRoot)
    {
        string mode = profile.ConfigurationMode.ToString().ToLowerInvariant();
        return $"--managed-instance {profile.Id} --instance-name \"{profile.DisplayName}\" --configuration-root \"{Path.GetFullPath(configurationRoot)}\" --configuration-mode {mode}";
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

    private static dynamic OpenService() => TryOpenService()
        ?? throw new InvalidOperationException("Task Scheduler is unavailable.");

    private static dynamic? TryOpenService()
    {
        Type? type = Type.GetTypeFromProgID("Schedule.Service");
        if (type is null) return null;
        dynamic service = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Task Scheduler could not be opened.");
        service.Connect();
        return service;
    }

    private static void DeleteIfPresent(dynamic folder, string taskName)
    {
        try { folder.DeleteTask(taskName, 0); }
        catch (Exception error) when (IsMissingTaskFailure(error)) { }
    }
}
