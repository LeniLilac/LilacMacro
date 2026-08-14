using LilacMacro.Core.LocalSession;
using LilacMacro.Windows.LocalSession;

namespace LilacMacro.Tests;

public sealed class ProvisioningJournalStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "LilacMacro-journal-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InvalidContentIsRejectedOnRead()
    {
        LocalSessionPaths paths = new(root, root, Path.Combine(root, "native"));
        await AtomicJsonFile.WriteAsync(
            paths.JournalPath,
            new LocalSessionProvisioningManifest { OwnerSid = "not-a-sid" });

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ProvisioningJournalStore(paths).ReadAsync());

        Assert.Contains("Owner SID", error.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
