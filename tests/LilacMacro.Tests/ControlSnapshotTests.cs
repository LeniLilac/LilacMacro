using System.Text;
using LilacMacro.Core.Services;

namespace LilacMacro.Tests;

public sealed class ControlSnapshotTests : IDisposable
{
    internal const string FixturePublicKey =
        "MCowBQYDK2VwAyEAY94MhIlLrxly3JfhhV30InhnIZ/QUIHfHMS+o4eLn84=";

    internal const string FixtureJson =
        """
        {"signature":"/Egv5BnjG1GkBY13iF2MPdBvcm3Hga4br/tWV86q3jIpc42viCb8fc6NCwWV6WEVozWa9ToVSo7vYjE2DF28BQ==","payload":{"schema":1,"revision":42,"generatedAt":"2026-08-14T12:00:00.000Z","expiresAt":"2026-08-14T12:10:00.000Z","game":{"available":true,"operatorAvailable":true,"observedPublic":true,"observedAt":"2026-08-14T11:59:30.000Z","message":"Update clear ? caf?"},"codes":[{"code":"WELCOME_2026","expiresAt":"2026-08-15T00:00:00.000Z"}],"schedules":[{"key":"gold-shop-reset","nextAt":"2026-08-15T00:00:00.000Z","cadenceSeconds":86400}],"disablements":[{"feature":"task.raid-shop","reason":"Temporarily paused","expiresAt":"2026-08-14T14:00:00.000Z"}],"release":{"version":"1.2.3","pageUrl":"https://github.com/LeniLilac/LilacMacro/releases/tag/v1.2.3","installerUrl":"https://github.com/LeniLilac/LilacMacro/releases/download/v1.2.3/LilacMacro-Setup.exe","publishedAt":"2026-08-14T11:00:00.000Z"}},"algorithm":"Ed25519","keyId":"node-fixture-1"}
        """;

    internal static readonly DateTimeOffset FixtureNow =
        DateTimeOffset.Parse("2026-08-14T12:05:00.000Z", System.Globalization.CultureInfo.InvariantCulture);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "LilacMacro.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Node_generated_snapshot_verifies_and_exposes_every_contract_field()
    {
        SignedControlSnapshot snapshot = CreateVerifier().Verify(
            Encoding.UTF8.GetBytes(FixtureJson),
            FixtureNow,
            minimumRevision: 42);

        Assert.Equal("node-fixture-1", snapshot.KeyId);
        Assert.Equal(42, snapshot.Payload.Revision);
        Assert.True(snapshot.Payload.Game.Available);
        Assert.Equal("WELCOME_2026", Assert.Single(snapshot.Payload.Codes).Code);
        Assert.Equal(ControlScheduleKeys.GoldShopReset, Assert.Single(snapshot.Payload.Schedules).Key);
        Assert.Equal("task.raid-shop", Assert.Single(snapshot.Payload.Disablements).Feature);
        Assert.Equal(new Version(1, 2, 3), snapshot.Payload.Release?.Version);
    }

    [Fact]
    public void Signature_verification_rejects_tampering_and_unknown_keys()
    {
        byte[] tampered = Encoding.UTF8.GetBytes(FixtureJson.Replace(
            "\"revision\":42",
            "\"revision\":43",
            StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => CreateVerifier().Verify(
            tampered,
            FixtureNow,
            minimumRevision: 0));

        ControlSnapshotVerifier unknownKeyVerifier = new(new Dictionary<string, string>
        {
            ["other-key"] = FixturePublicKey,
        });
        Assert.Throws<InvalidDataException>(() => unknownKeyVerifier.Verify(
            Encoding.UTF8.GetBytes(FixtureJson),
            FixtureNow,
            minimumRevision: 0));
    }

    [Fact]
    public void Strict_contract_rejects_unknown_and_duplicate_properties()
    {
        byte[] unknown = Encoding.UTF8.GetBytes(FixtureJson.Replace(
            "\"schema\":1,",
            "\"schema\":1,\"unexpected\":true,",
            StringComparison.Ordinal));
        byte[] duplicate = Encoding.UTF8.GetBytes(FixtureJson.Replace(
            "\"schema\":1,",
            "\"schema\":1,\"schema\":1,",
            StringComparison.Ordinal));

        Assert.Throws<InvalidDataException>(() => CreateVerifier().VerifySignature(unknown));
        Assert.Throws<InvalidDataException>(() => CreateVerifier().VerifySignature(duplicate));
    }

    [Fact]
    public void Freshness_rejects_rollback_expiry_future_generation_and_long_lifetime()
    {
        SignedControlSnapshot snapshot = CreateVerifier().VerifySignature(
            Encoding.UTF8.GetBytes(FixtureJson));

        Assert.Throws<InvalidDataException>(() => ControlSnapshotVerifier.ValidateFreshness(
            snapshot.Payload,
            FixtureNow,
            minimumRevision: 43));
        Assert.Throws<InvalidDataException>(() => ControlSnapshotVerifier.ValidateFreshness(
            snapshot.Payload,
            DateTimeOffset.Parse("2026-08-14T12:10:00.000Z"),
            minimumRevision: 42));
        Assert.Throws<InvalidDataException>(() => ControlSnapshotVerifier.ValidateFreshness(
            snapshot.Payload with { GeneratedAt = FixtureNow + TimeSpan.FromMinutes(2) },
            FixtureNow,
            minimumRevision: 42));
        Assert.Throws<InvalidDataException>(() => ControlSnapshotVerifier.ValidateFreshness(
            snapshot.Payload with { ExpiresAt = snapshot.Payload.GeneratedAt + TimeSpan.FromMinutes(16) },
            FixtureNow,
            minimumRevision: 42));
    }

    [Fact]
    public async Task Store_round_trips_signed_bytes_and_rejects_rollback()
    {
        string path = Path.Combine(_directory, "control.json");
        ControlSnapshotStore store = new(path, CreateVerifier());
        byte[] json = Encoding.UTF8.GetBytes(FixtureJson);

        ControlSnapshotCacheEntry saved = await store.SaveAsync(
            json,
            FixtureNow,
            minimumRevision: 42);
        ControlSnapshotCacheEntry? loaded = await store.LoadFreshAsync(FixtureNow);

        Assert.Equal(42, saved.Snapshot.Payload.Revision);
        Assert.NotNull(loaded);
        Assert.Equal(json, loaded.Json.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(
            json,
            FixtureNow,
            minimumRevision: 43));
    }

    [Fact]
    public async Task Store_preserves_signed_stale_revision_floor_but_does_not_apply_it()
    {
        string path = Path.Combine(_directory, "stale.json");
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(path, FixtureJson);
        ControlSnapshotStore store = new(path, CreateVerifier());

        ControlSnapshotCacheEntry? signed = await store.LoadAsync();
        ControlSnapshotCacheEntry? fresh = await store.LoadFreshAsync(
            DateTimeOffset.Parse("2026-08-14T12:11:00.000Z"));

        Assert.Equal(42, signed?.Snapshot.Payload.Revision);
        Assert.Null(fresh);
    }

    [Fact]
    public async Task Store_treats_corrupt_or_oversized_cache_as_a_miss()
    {
        string corruptPath = Path.Combine(_directory, "corrupt.json");
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(corruptPath, "not-json");
        Assert.Null(await new ControlSnapshotStore(corruptPath, CreateVerifier()).LoadAsync());

        string oversizedPath = Path.Combine(_directory, "oversized.json");
        await File.WriteAllBytesAsync(
            oversizedPath,
            new byte[ControlSnapshotVerifier.MaximumSnapshotBytes + 1]);
        Assert.Null(await new ControlSnapshotStore(oversizedPath, CreateVerifier()).LoadAsync());
    }

    [Fact]
    public async Task Store_honors_pre_cancelled_operations()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        ControlSnapshotStore store = new(Path.Combine(_directory, "control.json"), CreateVerifier());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.LoadAsync(cancellation.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    internal static ControlSnapshotVerifier CreateVerifier() => new(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["node-fixture-1"] = FixturePublicKey,
        });
}
