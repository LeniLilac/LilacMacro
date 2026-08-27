using System.Runtime.InteropServices;
using System.Security.Cryptography;
using LilacMacro.Core.LocalSession;

namespace LilacMacro.Windows.LocalSession;

public enum LocalSessionProbePurpose
{
    Install,
    Repair,
    Health,
}

public sealed class LocalSessionCompatibilityProbe(LocalSessionPaths paths)
{
    public async Task<LocalSessionCompatibilityResult> ProbeAsync(
        LocalSessionProbePurpose purpose = LocalSessionProbePurpose.Install,
        CancellationToken cancellationToken = default)
    {
        List<string> problems = [];
        string osBuild = Environment.OSVersion.Version.ToString();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) problems.Add("Windows 10 or later is required.");
        if (RuntimeInformation.OSArchitecture != Architecture.X64) problems.Add("Only Windows x64 is supported.");

        RemoteDesktopOwnershipInspector ownership = new(paths);
        problems.AddRange(purpose == LocalSessionProbePurpose.Install
            ? ownership.FindFreshInstallConflicts()
            : ownership.FindManagedConflicts());

        string termServicePath = Path.Combine(Environment.SystemDirectory, "termsrv.dll");
        string termServiceHash = await HashAsync(termServicePath, cancellationToken).ConfigureAwait(false);
        string termWrapHash = await HashAsync(paths.TermWrapPath, cancellationToken).ConfigureAwait(false);
        if (termServiceHash.Length == 0) problems.Add("The local TermService binary is missing.");
        if (termWrapHash.Length == 0) problems.Add("The pinned TermWrap binary is missing.");

        LocalSessionCompatibilityEvidence? evidence = null;
        bool usedCache = false;
        if (problems.Count == 0)
        {
            evidence = await ReadMatchingCacheAsync(osBuild, termServiceHash, termWrapHash, cancellationToken)
                .ConfigureAwait(false);
            usedCache = evidence is not null;
            if (evidence is null)
            {
                evidence = await RunNativeProbeAsync(osBuild, termServiceHash, termWrapHash, cancellationToken)
                    .ConfigureAwait(false);
                if (evidence.RequiredPatchesPassed)
                    await TryWriteCacheAsync(evidence, cancellationToken).ConfigureAwait(false);
            }
            if (!evidence.RequiredPatchesPassed)
                problems.AddRange(evidence.RequiredPatchDiagnostics);
        }

        if (purpose == LocalSessionProbePurpose.Install)
        {
            if (HasActiveRemoteSession()) problems.Add("An active Remote Desktop session exists.");
        }

        return new LocalSessionCompatibilityResult
        {
            IsCompatible = problems.Count == 0,
            UsedCachedEvidence = usedCache,
            OsBuild = osBuild,
            TermServiceSha256 = termServiceHash,
            TermWrapSha256 = termWrapHash,
            Evidence = evidence,
            Problems = problems,
        };
    }

    private async Task<LocalSessionCompatibilityEvidence> RunNativeProbeAsync(
        string osBuild,
        string termServiceHash,
        string termWrapHash,
        CancellationToken cancellationToken)
    {
        TermWrapNativePreflightResult run;
        try
        {
            run = await new TermWrapNativePreflight()
                .RunAsync(paths.TermWrapPath, TimeSpan.FromSeconds(15), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return CreateEvidence(osBuild, termServiceHash, termWrapHash, false,
                [$"The isolated native compatibility probe failed: {exception.Message}"], []);
        }

        TermWrapDiagnosticAssessment assessment = TermWrapDiagnosticPolicy.Assess(run.Diagnostics);
        List<string> requiredFailures = [.. assessment.RequiredFailures];
        if (!run.Started) requiredFailures.Add("The isolated native compatibility probe did not start.");
        if (!run.TargetModuleLoaded) requiredFailures.Add("The pinned TermWrap binary was not loaded by the isolated probe.");
        if (run.TimedOut) requiredFailures.Add("The isolated native compatibility probe exceeded its deadline.");
        bool passed = run.Started && run.TargetModuleLoaded && !run.TimedOut && assessment.RequiredPatchesPassed;
        return CreateEvidence(osBuild, termServiceHash, termWrapHash, passed, requiredFailures, assessment.Advisories);
    }

    private static LocalSessionCompatibilityEvidence CreateEvidence(
        string osBuild,
        string termServiceHash,
        string termWrapHash,
        bool passed,
        IReadOnlyList<string> failures,
        IReadOnlyList<string> advisories) => new()
        {
            OsBuild = osBuild,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            TermServiceSha256 = termServiceHash,
            TermWrapSha256 = termWrapHash,
            RequiredPatchesPassed = passed,
            RequiredPatchDiagnostics = failures,
            AdvisoryDiagnostics = advisories,
        };

    private async Task<LocalSessionCompatibilityEvidence?> ReadMatchingCacheAsync(
        string osBuild,
        string termServiceHash,
        string termWrapHash,
        CancellationToken cancellationToken)
    {
        try
        {
            LocalSessionCompatibilityEvidence? cached =
                await AtomicJsonFile.ReadAsync<LocalSessionCompatibilityEvidence>(paths.CompatibilityCachePath, cancellationToken)
                    .ConfigureAwait(false);
            return TermWrapCompatibilityCachePolicy.IsReusable(
                cached,
                osBuild,
                RuntimeInformation.OSArchitecture.ToString(),
                termServiceHash,
                termWrapHash) ? cached : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private async Task TryWriteCacheAsync(LocalSessionCompatibilityEvidence evidence, CancellationToken cancellationToken)
    {
        try { await AtomicJsonFile.WriteAsync(paths.CompatibilityCachePath, evidence, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return string.Empty;
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static bool HasActiveRemoteSession()
    {
        if (!WTSEnumerateSessions(nint.Zero, 0, 1, out nint buffer, out int count)) return true;
        try
        {
            int size = Marshal.SizeOf<WtsSessionInfo>();
            for (int index = 0; index < count; index++)
            {
                WtsSessionInfo session = Marshal.PtrToStructure<WtsSessionInfo>(buffer + (index * size));
                if (session.State != 0) continue;
                if (!WTSQuerySessionInformation(nint.Zero, session.SessionId, 16, out nint value, out int bytes)) continue;
                try { if (bytes >= 2 && Marshal.ReadInt16(value) == 2) return true; }
                finally { WTSFreeMemory(value); }
            }
            return false;
        }
        finally { WTSFreeMemory(buffer); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WtsSessionInfo { public int SessionId; public string WinStationName; public int State; }

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateSessions(nint server, int reserved, int version, out nint sessions, out int count);

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(nint server, int sessionId, int infoClass, out nint buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(nint memory);
}
