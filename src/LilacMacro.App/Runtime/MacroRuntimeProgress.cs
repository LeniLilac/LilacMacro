using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LilacMacro.App.Views;

namespace LilacMacro.App.Runtime;

internal sealed record MacroRuntimeProgressSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public List<MacroPlanRuntimeProgress> Plans { get; init; } = [];
}

internal sealed record MacroPlanRuntimeProgress
{
    public Guid RuntimeId { get; init; }

    public List<MacroTaskRuntimeProgress> Tasks { get; init; } = [];

    public List<MacroLoopRuntimeProgress> Loops { get; init; } = [];
}

internal sealed record MacroTaskRuntimeProgress
{
    public Guid RuntimeId { get; init; }

    public int Victories { get; init; }

    public int Defeats { get; init; }

    public DateTimeOffset? UtilityDueAtUtc { get; init; }
}

internal sealed record MacroLoopRuntimeProgress
{
    public Guid RuntimeId { get; init; }

    public int CompletedRuns { get; init; }
}

internal static class MacroRuntimeProgressMapper
{
    public static MacroRuntimeProgressSnapshot Capture(
        IReadOnlyList<PlanPrototype> plans,
        IReadOnlyDictionary<PlanTaskPrototype, int> victories,
        IReadOnlyDictionary<PlanTaskPrototype, int> defeats,
        IReadOnlyDictionary<PlanLoopPrototype, int> completedRuns,
        IReadOnlyDictionary<PlanTaskPrototype, DateTimeOffset> utilityDueAt)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(victories);
        ArgumentNullException.ThrowIfNull(defeats);
        ArgumentNullException.ThrowIfNull(completedRuns);
        ArgumentNullException.ThrowIfNull(utilityDueAt);

        return new MacroRuntimeProgressSnapshot
        {
            Plans = plans.Select(plan => new MacroPlanRuntimeProgress
            {
                RuntimeId = plan.RuntimeId,
                Tasks = EnumerateTasks(plan).Select(task => new MacroTaskRuntimeProgress
                {
                    RuntimeId = task.RuntimeId,
                    Victories = victories.GetValueOrDefault(task),
                    Defeats = defeats.GetValueOrDefault(task),
                    UtilityDueAtUtc = utilityDueAt.GetValueOrDefault(task),
                }).ToList(),
                Loops = EnumerateLoops(plan).Select(loop => new MacroLoopRuntimeProgress
                {
                    RuntimeId = loop.RuntimeId,
                    CompletedRuns = completedRuns.GetValueOrDefault(loop),
                }).ToList(),
            }).ToList(),
        };
    }

    public static void Apply(
        IReadOnlyList<PlanPrototype> plans,
        MacroRuntimeProgressSnapshot snapshot,
        IDictionary<PlanTaskPrototype, int> victories,
        IDictionary<PlanTaskPrototype, int> defeats,
        IDictionary<PlanLoopPrototype, int> completedRuns,
        IDictionary<PlanTaskPrototype, DateTimeOffset> utilityDueAt)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(victories);
        ArgumentNullException.ThrowIfNull(defeats);
        ArgumentNullException.ThrowIfNull(completedRuns);
        ArgumentNullException.ThrowIfNull(utilityDueAt);

        victories.Clear();
        defeats.Clear();
        completedRuns.Clear();
        utilityDueAt.Clear();
        foreach (PlanPrototype plan in plans)
        {
            foreach (PlanLoopPrototype loop in EnumerateLoops(plan)) loop.CompletedRuns = 0;
            MacroPlanRuntimeProgress? savedPlan = snapshot.Plans
                .FirstOrDefault(candidate => candidate.RuntimeId == plan.RuntimeId);
            if (savedPlan is null) continue;

            foreach (PlanTaskPrototype task in EnumerateTasks(plan))
            {
                MacroTaskRuntimeProgress? savedTask = savedPlan.Tasks
                    .FirstOrDefault(candidate => candidate.RuntimeId == task.RuntimeId);
                if (savedTask is null) continue;
                if (savedTask.Victories > 0) victories[task] = savedTask.Victories;
                if (savedTask.Defeats > 0) defeats[task] = savedTask.Defeats;
                if (task.Mode == PlanTaskMode.Utilities && savedTask.UtilityDueAtUtc is DateTimeOffset dueAt)
                    utilityDueAt[task] = dueAt;
            }

            foreach (PlanLoopPrototype loop in EnumerateLoops(plan))
            {
                MacroLoopRuntimeProgress? savedLoop = savedPlan.Loops
                    .FirstOrDefault(candidate => candidate.RuntimeId == loop.RuntimeId);
                if (savedLoop is null) continue;
                completedRuns[loop] = savedLoop.CompletedRuns;
                loop.CompletedRuns = savedLoop.CompletedRuns;
            }
        }
    }

    private static IEnumerable<PlanTaskPrototype> EnumerateTasks(PlanPrototype plan) =>
        EnumerateBlocks(plan.Blocks).OfType<PlanTaskPrototype>();

    private static IEnumerable<PlanLoopPrototype> EnumerateLoops(PlanPrototype plan) =>
        EnumerateBlocks(plan.Blocks).OfType<PlanLoopPrototype>();

    private static IEnumerable<PlanBlockPrototype> EnumerateBlocks(
        IEnumerable<PlanBlockPrototype> blocks)
    {
        foreach (PlanBlockPrototype block in blocks)
        {
            yield return block;
            if (block is PlanLoopPrototype loop)
            {
                foreach (PlanBlockPrototype child in EnumerateBlocks(loop.Children))
                    yield return child;
            }
        }
    }
}

internal sealed class MacroRuntimeProgressStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _saveSync = new();
    private Task _pendingSave = Task.CompletedTask;

    public MacroRuntimeProgressStore(string configurationRoot, string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instanceId)))[..16];
        _path = Path.Combine(configurationRoot, $"macro-runtime-progress-{suffix}.json");
    }

    public async Task<MacroRuntimeProgressSnapshot> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return new MacroRuntimeProgressSnapshot();
        try
        {
            await using FileStream stream = File.OpenRead(_path);
            MacroRuntimeProgressSnapshot? snapshot = await JsonSerializer.DeserializeAsync<MacroRuntimeProgressSnapshot>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return IsValid(snapshot) ? snapshot! : new MacroRuntimeProgressSnapshot();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new MacroRuntimeProgressSnapshot();
        }
    }

    public Task QueueSave(MacroRuntimeProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_saveSync)
        {
            Task previous = _pendingSave;
            _pendingSave = SaveAfterAsync(previous, snapshot);
            return _pendingSave;
        }
    }

    public Task FlushAsync()
    {
        lock (_saveSync) return _pendingSave;
    }

    private async Task SaveAfterAsync(Task previous, MacroRuntimeProgressSnapshot snapshot)
    {
        try { await previous.ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        await SaveAtomicAsync(snapshot).ConfigureAwait(false);
    }

    private async Task SaveAtomicAsync(
        MacroRuntimeProgressSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        string directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Runtime progress path has no parent directory.");
        Directory.CreateDirectory(directory);
        await using FileStream writeLock = await AcquireWriteLockAsync(cancellationToken).ConfigureAwait(false);
        string temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                8192,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task<FileStream> AcquireWriteLockAsync(CancellationToken cancellationToken)
    {
        string lockPath = _path + ".write.lock";
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsValid(MacroRuntimeProgressSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.SchemaVersion != MacroRuntimeProgressSnapshot.CurrentSchemaVersion ||
            snapshot.Plans is null) return false;
        foreach (MacroPlanRuntimeProgress? plan in snapshot.Plans)
        {
            if (plan is null || plan.RuntimeId == Guid.Empty || plan.Tasks is null || plan.Loops is null)
                return false;
            if (plan.Tasks.Any(task => task is null || task.RuntimeId == Guid.Empty ||
                                       task.Victories < 0 || task.Defeats < 0) ||
                plan.Loops.Any(loop => loop is null || loop.RuntimeId == Guid.Empty || loop.CompletedRuns < 0))
                return false;
        }
        return true;
    }
}
