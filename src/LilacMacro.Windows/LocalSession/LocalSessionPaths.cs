namespace LilacMacro.Windows.LocalSession;

public sealed record LocalSessionPaths(
    string ProgramDataRoot,
    string InstallRoot,
    string NativePayloadRoot)
{
    public static LocalSessionPaths CreateDefault(string installRoot) => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LilacMacro"),
        Path.GetFullPath(installRoot),
        Path.Combine(Path.GetFullPath(installRoot), "native", "termwrap", "v0.6"));

    public string SessionRoot => Path.Combine(ProgramDataRoot, "Session");
    public string RunnerRoot => Path.Combine(ProgramDataRoot, "Runner");
    public string JournalPath => Path.Combine(SessionRoot, "provisioning.json");
    public string StatusPath => Path.Combine(SessionRoot, "status.json");
    public string SnapshotPath => Path.Combine(RunnerRoot, "runtime-snapshot.json");
    public string RuntimeRoot => Path.Combine(RunnerRoot, "Runtime");
    public string OcrRoot => Path.Combine(RunnerRoot, "Ocr");
    public string ProfilePolicyPath => Path.Combine(RunnerRoot, "runner-profile-policy.json");
    public string ProfileReceiptPath => Path.Combine(RunnerRoot, "runner-profile-receipt.json");
    public string ProfileFailurePath => Path.Combine(RunnerRoot, "runner-profile-failure.json");
    public string CompatibilityCachePath => Path.Combine(SessionRoot, "compatibility-cache.json");
    public string CredentialTarget => $"TERMSRV/127.0.0.1:{TermServiceConfigurationManager.LocalPort}";
    public string PayloadManifestPath => Path.Combine(NativePayloadRoot, "payload.json");
    public string TermWrapPath => Path.Combine(NativePayloadRoot, "x64", "TermWrap.dll");
    public string WorkerPath => Path.Combine(InstallRoot, "LilacMacro.SessionWorker.exe");
}
