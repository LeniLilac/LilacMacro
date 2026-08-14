using LilacMacro.App.Debugging;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Tests;

public sealed class WireVisualLocatorStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "LilacMacro-wire-locator-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoadRoundTripLocatorMetadata()
    {
        WireVisualLocator expected = new(
            1,
            "wire-lobby-store-any",
            "LOBBY",
            "Store",
            new PixelRect(76, 229, 48, 22));
        WireVisualLocatorStore store = new();

        string path = await store.SaveAsync(_root, expected);
        WireVisualLocator actual = await store.LoadAsync(_root, expected.ProfileId);

        Assert.True(File.Exists(path));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PathForRejectsUnsafeProfileId()
    {
        WireVisualLocatorStore store = new();

        Assert.Throws<ArgumentException>(() => store.PathFor(_root, "../outside"));
        Assert.Throws<ArgumentException>(() => store.PathFor(_root, ".."));
        Assert.Throws<ArgumentException>(() => store.PathFor(_root, "."));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
