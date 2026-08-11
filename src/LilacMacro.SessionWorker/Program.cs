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
                return ApplyProfilePolicy(paths, policyPath, receiptPath);
            }
            return 64;
        }
        catch { return 1; }
    }

    private static string BuildVersion() => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    private static bool PathsEqual(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static int ApplyProfilePolicy(LocalSessionPaths paths, string policyPath, string receiptPath)
    {
        RunnerProfileStore store = new(paths);
        try
        {
            if (!PathsEqual(policyPath, paths.ProfilePolicyPath) ||
                !PathsEqual(receiptPath, paths.ProfileReceiptPath))
            {
                return 64;
            }
            RunnerProfilePolicy policy = store.ReadPolicyAsync().GetAwaiter().GetResult()
                ?? throw new InvalidDataException("Runner profile policy is missing.");
            RunnerProfileReceipt receipt = new RunnerProfilePolicyApplier()
                .ApplyAsync(policy, CancellationToken.None).GetAwaiter().GetResult();
            store.WriteReceiptAsync(receipt).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception error)
        {
            try
            {
                store.WriteFailureAsync(new RunnerProfileFailure
                {
                    FailureCode = FailureCode(error),
                    Detail = SafeDetail(error.Message),
                }).GetAwaiter().GetResult();
            }
            catch (Exception recordingError) when (recordingError is IOException or UnauthorizedAccessException)
            {
            }
            return 1;
        }
    }

    private static string FailureCode(Exception error) => error switch
    {
        UnauthorizedAccessException => "profile-policy-access-denied",
        IOException => "profile-policy-io-failed",
        InvalidDataException => "profile-policy-invalid",
        _ => "profile-policy-application-failed",
    };

    private static string SafeDetail(string detail)
    {
        string flattened = detail.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flattened.Length <= 500 ? flattened : flattened[..500];
    }
}
