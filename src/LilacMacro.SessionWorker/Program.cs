using LilacMacro.Core.LocalSession;
using LilacMacro.Runtime.Runner;
using LilacMacro.Windows.LocalSession;

namespace LilacMacro.SessionWorker;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            LocalSessionPaths paths = LocalSessionPaths.CreateDefault(AppContext.BaseDirectory);
            if (args is ["--serve"])
            {
                new SessionWorkerHost(paths, BuildVersion(), new HeadlessSessionRuntime(paths))
                    .RunAsync(CancellationToken.None).GetAwaiter().GetResult();
                return 0;
            }
            if (args is ["--apply-profile-policy", string policyPath, string receiptPath])
            {
                if (!PathsEqual(policyPath, paths.ProfilePolicyPath) || !PathsEqual(receiptPath, paths.ProfileReceiptPath)) return 64;
                RunnerProfileStore store = new(paths);
                RunnerProfilePolicy policy = store.ReadPolicyAsync().GetAwaiter().GetResult()
                    ?? throw new InvalidDataException("Runner profile policy is missing.");
                RunnerProfileReceipt receipt = new RunnerProfilePolicyApplier().ApplyAsync(policy, CancellationToken.None).GetAwaiter().GetResult();
                store.WriteReceiptAsync(receipt).GetAwaiter().GetResult();
                return 0;
            }
            return 64;
        }
        catch { return 1; }
    }

    private static string BuildVersion() => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    private static bool PathsEqual(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
