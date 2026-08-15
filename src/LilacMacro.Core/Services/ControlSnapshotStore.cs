using System.Text.Json;

namespace LilacMacro.Core.Services;

public sealed record ControlSnapshotCacheEntry(
    SignedControlSnapshot Snapshot,
    ReadOnlyMemory<byte> Json);

public sealed class ControlSnapshotStore
{
    private readonly string _path;
    private readonly ControlSnapshotVerifier _verifier;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ControlSnapshotStore(string path, ControlSnapshotVerifier verifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    }

    public async Task<ControlSnapshotCacheEntry?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ControlSnapshotCacheEntry?> LoadFreshAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ControlSnapshotCacheEntry? cached = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (cached is null) return null;
        try
        {
            ControlSnapshotVerifier.ValidateFreshness(
                cached.Snapshot.Payload,
                now,
                cached.Snapshot.Payload.Revision);
            return cached;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    public async Task<ControlSnapshotCacheEntry> SaveAsync(
        ReadOnlyMemory<byte> json,
        DateTimeOffset now,
        long minimumRevision,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ControlSnapshotCacheEntry? current = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            long revisionFloor = Math.Max(
                minimumRevision,
                current?.Snapshot.Payload.Revision ?? 0);
            SignedControlSnapshot snapshot = _verifier.Verify(json, now, revisionFloor);
            string? directory = Path.GetDirectoryName(_path);
            if (directory is null)
                throw new InvalidOperationException("Control snapshot path has no parent directory.");
            Directory.CreateDirectory(directory);

            string temporary = _path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await using (FileStream stream = new(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    8192,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                File.Move(temporary, _path, overwrite: true);
            }
            finally
            {
                TryDelete(temporary);
            }
            return new ControlSnapshotCacheEntry(snapshot, json.ToArray());
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ControlSnapshotCacheEntry?> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return null;
        try
        {
            FileInfo info = new(_path);
            if (info.Length is < 2 or > ControlSnapshotVerifier.MaximumSnapshotBytes) return null;
            byte[] json = new byte[checked((int)info.Length)];
            await using FileStream stream = new(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                8192,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await stream.ReadExactlyAsync(json, cancellationToken).ConfigureAwait(false);
            return new ControlSnapshotCacheEntry(_verifier.VerifySignature(json), json);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException or
            InvalidDataException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
