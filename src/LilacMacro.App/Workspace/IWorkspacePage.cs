namespace LilacMacro.App.Workspace;

public interface IWorkspacePage
{
    Task RefreshAsync();
}

public interface IStoppableWorkspacePage : IWorkspacePage
{
    Task StopAsync();
}
