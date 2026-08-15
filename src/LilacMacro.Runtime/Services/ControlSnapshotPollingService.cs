using System.Net.Http;
using System.Text.Json;
using LilacMacro.Core.Services;

namespace LilacMacro.Runtime.Services;

public enum ControlPollState
{
    Fresh,
    OfflineUsingLastKnownGood,
    OfflineWithoutSnapshot,
    RejectedUsingLastKnownGood,
    RejectedWithoutSnapshot,
}

public sealed record ControlPollResult(
    ControlPollState State,
    SignedControlSnapshot? Snapshot,
    DateTimeOffset ObservedAt);

public sealed class ControlSnapshotPollingService
{
    public static readonly TimeSpan BasePollInterval = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan MaximumPollJitter = TimeSpan.FromMinutes(1);

    private readonly IControlSnapshotTransport _transport;
    private readonly ControlSnapshotStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan> _nextJitter;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SignedControlSnapshot? _current;
    private long _revisionFloor;
    private bool _initialized;

    public ControlSnapshotPollingService(
        IControlSnapshotTransport transport,
        ControlSnapshotStore store,
        TimeProvider? timeProvider = null)
        : this(
            transport,
            store,
            timeProvider ?? TimeProvider.System,
            () => TimeSpan.FromMilliseconds(
                Random.Shared.NextInt64(0, checked((long)MaximumPollJitter.TotalMilliseconds + 1))))
    { }

    internal ControlSnapshotPollingService(
        IControlSnapshotTransport transport,
        ControlSnapshotStore store,
        TimeProvider timeProvider,
        Func<TimeSpan> nextJitter)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _nextJitter = nextJitter ?? throw new ArgumentNullException(nameof(nextJitter));
    }

    public event EventHandler<ControlPollResult>? Refreshed;

    public long RevisionFloor => Interlocked.Read(ref _revisionFloor);

    public SignedControlSnapshot? Current
    {
        get
        {
            SignedControlSnapshot? snapshot = Volatile.Read(ref _current);
            if (snapshot is null) return null;
            try
            {
                ControlSnapshotVerifier.ValidateFreshness(
                    snapshot.Payload,
                    _timeProvider.GetUtcNow(),
                    snapshot.Payload.Revision);
                return snapshot;
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }
    }

    public async Task<ControlPollResult> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        ControlPollResult result;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
            DateTimeOffset now = _timeProvider.GetUtcNow();
            try
            {
                ReadOnlyMemory<byte> json = await _transport.GetAsync(cancellationToken)
                    .ConfigureAwait(false);
                ControlSnapshotCacheEntry saved = await _store.SaveAsync(
                    json,
                    now,
                    RevisionFloor,
                    cancellationToken).ConfigureAwait(false);
                Publish(saved.Snapshot);
                result = new ControlPollResult(ControlPollState.Fresh, saved.Snapshot, now);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = OfflineResult(now);
            }
            catch (Exception exception) when (IsRejected(exception))
            {
                result = RejectedResult(now);
            }
            catch (Exception exception) when (IsOffline(exception))
            {
                result = OfflineResult(now);
            }
        }
        finally
        {
            _gate.Release();
        }
        Refreshed?.Invoke(this, result);
        return result;
    }

    public Task RunAsync(CancellationToken cancellationToken) =>
        RunAsync(() => true, cancellationToken);

    public Task RunAsync(Func<bool> isEnabled, CancellationToken cancellationToken) =>
        RunAsync(_ => Task.FromResult(isEnabled()), cancellationToken);

    public async Task RunAsync(
        Func<CancellationToken, Task<bool>> isEnabled,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(isEnabled);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await isEnabled(cancellationToken).ConfigureAwait(false))
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            TimeSpan jitter = _nextJitter();
            if (jitter < TimeSpan.Zero || jitter > MaximumPollJitter)
                throw new InvalidOperationException("Control polling jitter was outside its bound.");
            await Task.Delay(
                BasePollInterval + jitter,
                _timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        ControlSnapshotCacheEntry? cached = await _store.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null)
        {
            Interlocked.Exchange(ref _revisionFloor, cached.Snapshot.Payload.Revision);
            try
            {
                ControlSnapshotVerifier.ValidateFreshness(
                    cached.Snapshot.Payload,
                    _timeProvider.GetUtcNow(),
                    cached.Snapshot.Payload.Revision);
                Volatile.Write(ref _current, cached.Snapshot);
            }
            catch (InvalidDataException) { }
        }
        _initialized = true;
    }

    private void Publish(SignedControlSnapshot snapshot)
    {
        Interlocked.Exchange(ref _revisionFloor, snapshot.Payload.Revision);
        Volatile.Write(ref _current, snapshot);
    }

    private ControlPollResult OfflineResult(DateTimeOffset now)
    {
        SignedControlSnapshot? current = Current;
        return new ControlPollResult(
            current is null
                ? ControlPollState.OfflineWithoutSnapshot
                : ControlPollState.OfflineUsingLastKnownGood,
            current,
            now);
    }

    private ControlPollResult RejectedResult(DateTimeOffset now)
    {
        SignedControlSnapshot? current = Current;
        return new ControlPollResult(
            current is null
                ? ControlPollState.RejectedWithoutSnapshot
                : ControlPollState.RejectedUsingLastKnownGood,
            current,
            now);
    }

    private static bool IsRejected(Exception exception) => exception is
        InvalidDataException or JsonException;

    private static bool IsOffline(Exception exception) => exception is
        HttpRequestException or IOException or UnauthorizedAccessException;
}
