using System.Text.Json;
using System.Text.RegularExpressions;
using LilacMacro.App.Debugging;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.App.Workspace;
using LilacMacro.Core.LocalSession;
using LilacMacro.Core.Ocr;
using LilacMacro.Core.Placements;
using LilacMacro.Windows.LocalSession;
using LilacMacro.Runtime.Normalization;

namespace LilacMacro.Runtime.Runner;

public sealed class HeadlessSessionRuntime(LocalSessionPaths paths) : ISessionWorkerRuntime
{
    private const string ContextFileName = "state-contexts.json";
    private const string PlacementDirectoryName = "placements";
    private const string ProfileDirectoryName = "visual-profiles";
    private const string ChallengeFileName = "challenge-rotation.json";

    public bool IsAvailable(out string detail)
    {
        string worker = Path.Combine(paths.InstallRoot, "tools", "ocr_worker.py");
        string setup = Path.Combine(paths.InstallRoot, "scripts", "Setup-Ocr.ps1");
        bool available = File.Exists(worker) && File.Exists(setup);
        detail = available
            ? "Shared headless workflow runtime is available."
            : "The runner OCR payload is incomplete.";
        return available;
    }

    public async Task RunAsync(
        RunnerRuntimeSnapshot snapshot,
        SessionStartRequest request,
        IProgress<SessionRuntimeProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        PrivateServerRejoinService.Validate(request.PrivateServerLink);

        string revisionRoot = await MaterializeAsync(snapshot, cancellationToken).ConfigureAwait(false);
        ApplyRuntimeEnvironment(revisionRoot);
        DeepDebugSessionService deepDebug = new(paths.RuntimeRoot);
        using WorkspaceController workspace = new(deepDebug);
        using OcrRunner ocr = new(deepDebug, paths.OcrRoot) { KeepLoaded = true };
        await EnsureOcrAsync(ocr, snapshot, progress, cancellationToken).ConfigureAwait(false);
        await workspace.InitializeAsync(cancellationToken).ConfigureAwait(false);

        StoryWireTestRunner runner = new(workspace, ocr, deepDebug);
        UiScaleNormalizer uiScale = new(workspace, ocr, deepDebug);
        PrivateServerRejoinService rejoin = new(workspace, ocr);
        PlacementSetupStore placements = new(Path.Combine(revisionRoot, PlacementDirectoryName));
        ChallengePlacementResolver challengePlacements = new(placements);
        Dictionary<string, int> wins = snapshot.Tasks.ToDictionary(task => task.Id, _ => 0, StringComparer.Ordinal);
        Dictionary<string, int> losses = snapshot.Tasks.ToDictionary(task => task.Id, _ => 0, StringComparer.Ordinal);
        Dictionary<string, DateTimeOffset> blockedUntil = new(StringComparer.Ordinal);
        string device = SelectDevice(ocr, snapshot.PreferGpu);

        await ResetLobbyAsync(
            rejoin,
            uiScale,
            request.PrivateServerLink,
            device,
            detail => progress.Report(new SessionRuntimeProgress
            {
                Stage = "startup-normalization",
                Detail = detail,
            }),
            cancellationToken).ConfigureAwait(false);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunnerTaskSnapshot? task = SelectTask(snapshot.Tasks, wins, blockedUntil);
            if (task is null)
            {
                if (snapshot.Tasks.All(candidate => candidate.Mode == RunnerTaskMode.Challenge || wins[candidate.Id] >= candidate.Target))
                    return;
                DateTimeOffset next = blockedUntil.Values.DefaultIfEmpty(DateTimeOffset.UtcNow.AddSeconds(5)).Min();
                await Task.Delay(Max(TimeSpan.Zero, next - DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
                continue;
            }

            Report(progress, task, "task-started", wins, losses, $"RUN {task.Mode} {task.Route}".Trim());
            StoryWireTestOptions options = await CreateOptionsAsync(
                task,
                snapshot.KeyBindings,
                device,
                placements,
                challengePlacements,
                cancellationToken).ConfigureAwait(false);
            StoryWireTestResult result = await runner.RunAsync(
                options,
                new InlineProgress<StoryWireProgress>(value => Report(
                    progress,
                    task,
                    StoryWireTestRunner.Format(value.Stage),
                    wins,
                    losses,
                    value.Detail)),
                cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded) throw new InvalidOperationException(result.Status);

            if (result.UnavailableUntilUtc is DateTimeOffset unavailableUntil)
            {
                blockedUntil[task.Id] = unavailableUntil;
                Report(progress, task, "blocked", wins, losses, result.Status);
            }
            else if (result.Status.StartsWith("VICTORY", StringComparison.Ordinal))
            {
                wins[task.Id]++;
                Report(progress, task, "victory", wins, losses, result.Status);
            }
            else
            {
                losses[task.Id]++;
                Report(progress, task, "defeat", wins, losses, result.Status);
                if (losses[task.Id] > task.DefeatRetries)
                    throw new InvalidOperationException($"{task.Id} exceeded its defeat retry limit.");
            }

            await ResetLobbyAsync(
                rejoin,
                uiScale,
                request.PrivateServerLink,
                device,
                detail => Report(progress, task, "lobby-reset", wins, losses, detail),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ResetLobbyAsync(
        PrivateServerRejoinService rejoin,
        UiScaleNormalizer uiScale,
        string privateServerLink,
        string device,
        Action<string> status,
        CancellationToken cancellationToken)
    {
        await rejoin.RejoinAndVerifyLobbyAsync(
            privateServerLink,
            device,
            status,
            cancellationToken).ConfigureAwait(false);
        await uiScale.NormalizeAsync(device, status, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> MaterializeAsync(RunnerRuntimeSnapshot snapshot, CancellationToken cancellationToken)
    {
        string revisionRoot = Path.Combine(paths.RuntimeRoot, snapshot.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(revisionRoot);
        await AtomicJsonFile.WriteAsync(
            Path.Combine(revisionRoot, ContextFileName),
            snapshot.StateContexts,
            cancellationToken).ConfigureAwait(false);

        string placementRoot = Path.Combine(revisionRoot, PlacementDirectoryName);
        Directory.CreateDirectory(placementRoot);
        if (snapshot.PlacementSetups.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Runner placement snapshot is not an object.");
        foreach (JsonProperty document in snapshot.PlacementSetups.EnumerateObject())
        {
            string fileName = Path.GetFileName(document.Name);
            if (!string.Equals(fileName, document.Name, StringComparison.Ordinal) || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Runner placement snapshot contains an unsafe file name.");
            await AtomicJsonFile.WriteAsync(Path.Combine(placementRoot, fileName), document.Value, cancellationToken).ConfigureAwait(false);
        }
        return revisionRoot;
    }

    private static void ApplyRuntimeEnvironment(string revisionRoot)
    {
        Environment.SetEnvironmentVariable("LILACMACRO_RUNNER_STATE_CONTEXTS", Path.Combine(revisionRoot, ContextFileName));
        Environment.SetEnvironmentVariable("LILACMACRO_RUNNER_PLACEMENTS", Path.Combine(revisionRoot, PlacementDirectoryName));
        Environment.SetEnvironmentVariable("LILACMACRO_RUNNER_VISUAL_PROFILES", Path.Combine(revisionRoot, ProfileDirectoryName));
        Environment.SetEnvironmentVariable("LILACMACRO_RUNNER_CHALLENGE_ROTATION", Path.Combine(revisionRoot, ChallengeFileName));
    }

    private static async Task EnsureOcrAsync(
        OcrRunner ocr,
        RunnerRuntimeSnapshot snapshot,
        IProgress<SessionRuntimeProgress> progress,
        CancellationToken cancellationToken)
    {
        if (!ocr.IsInstalled)
        {
            progress.Report(new SessionRuntimeProgress { Stage = "ocr-setup", Detail = "Installing the runner OCR runtime." });
            await ocr.SetupAsync(snapshot.PreferGpu ? OcrRunner.GpuDevice : OcrRunner.CpuDevice, cancellationToken).ConfigureAwait(false);
        }
        string device = SelectDevice(ocr, snapshot.PreferGpu);
        await ocr.WarmUpAsync(snapshot.OcrModel, device, cancellationToken).ConfigureAwait(false);
        progress.Report(new SessionRuntimeProgress { Stage = "ocr-ready", Detail = $"OCR ready on {device}." });
    }

    private static RunnerTaskSnapshot? SelectTask(
        IReadOnlyList<RunnerTaskSnapshot> tasks,
        IReadOnlyDictionary<string, int> wins,
        IReadOnlyDictionary<string, DateTimeOffset> blockedUntil)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return tasks
            .OrderBy(task => task.Priority)
            .FirstOrDefault(task =>
                (task.Mode == RunnerTaskMode.Challenge || wins[task.Id] < task.Target) &&
                (!blockedUntil.TryGetValue(task.Id, out DateTimeOffset until) || now >= until));
    }

    private static async Task<StoryWireTestOptions> CreateOptionsAsync(
        RunnerTaskSnapshot task,
        IReadOnlyDictionary<string, int?> keys,
        string device,
        PlacementSetupStore placements,
        ChallengePlacementResolver challengePlacements,
        CancellationToken cancellationToken)
    {
        WireGameMode gameMode = task.Mode switch
        {
            RunnerTaskMode.Raid => WireGameMode.Raid,
            RunnerTaskMode.Challenge => WireGameMode.Challenge,
            _ => WireGameMode.Story,
        };
        (string map, StoryAct act) = gameMode == WireGameMode.Challenge ? ("AUTO", StoryAct.Act1) : ParseRoute(task.Route);
        int team = gameMode == WireGameMode.Challenge
            ? await challengePlacements.ResolveCommonTeamAsync(cancellationToken).ConfigureAwait(false)
            : await ResolveTeamAsync(gameMode, map, act, placements, cancellationToken).ConfigureAwait(false);
        RegularChallengeType[] challengeTypes = ChallengeTypes(task);
        return new StoryWireTestOptions(
            DebugEvidenceMode.ImageWithOcrFallback,
            gameMode,
            team,
            map,
            act,
            task.HardMode ? StoryDifficulty.Hard : StoryDifficulty.Normal,
            challengeTypes,
            new StoryWireNavigationKeys(OptionalKey(keys, "PlayMenu"), OptionalKey(keys, "UnitInventory"), OptionalKey(keys, "AreasMenu")),
            new PlacementRuntimeKeys(
                RequiredKey(keys, "QuickPlacement"),
                RequiredKey(keys, "CancelPlacement"),
                RequiredKey(keys, "ChangeTargeting"),
                RequiredKey(keys, "ChangeAutoUpgrade"),
                RequiredKey(keys, "UpgradeUnit"),
                RequiredKey(keys, "SellUnit"),
                RequiredKey(keys, "MacroToggle")),
            RequiredKey(keys, "ShiftLock"),
            device,
            RunMatchRuntime: true,
            RepeatStage: false);
    }

    private static async Task<int> ResolveTeamAsync(
        WireGameMode mode,
        string map,
        StoryAct act,
        PlacementSetupStore placements,
        CancellationToken cancellationToken)
    {
        string mapId = mode == WireGameMode.Raid ? $"raid-spirit-city-{RouteId(act)}" : $"story-{Slug(map)}";
        PlacementMapDefinition definition = PlacementMapCatalog.Definitions.First(candidate => candidate.Id == mapId);
        PlacementSetupDocument document = await placements.LoadAsync(mapId, cancellationToken).ConfigureAwait(false);
        PlacementRouteDefinition routeDefinition = PlacementRouteCatalog.For(definition)
            .FirstOrDefault(candidate => candidate.Id == RouteId(act))
            ?? PlacementRouteCatalog.For(definition).First(candidate => candidate.IsShared);
        return PlacementRouteCatalog.EffectiveRoute(document, routeDefinition).TeamSlot;
    }

    private static RegularChallengeType[] ChallengeTypes(RunnerTaskSnapshot task)
    {
        if (task.Mode != RunnerTaskMode.Challenge) return [];
        List<RegularChallengeType> result = [];
        if (task.RunTrait) result.Add(RegularChallengeType.Trait);
        if (task.RunStat) result.Add(RegularChallengeType.Stat);
        if (task.RunSprite) result.Add(RegularChallengeType.Sprite);
        if (result.Count == 0) throw new InvalidDataException("Challenge task has no enabled types.");
        return [.. result];
    }

    private static (string Map, StoryAct Act) ParseRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route)) throw new InvalidDataException("Task route has no map.");
        Match match = Regex.Match(route, @"\b(Act\s+[1-5]|Infinite|Mastery)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        string map = (match.Success ? route[..match.Index] : route).Trim().TrimEnd('\u00b7', '\u00c2', '-', '|', '/').Trim();
        if (string.IsNullOrWhiteSpace(map)) throw new InvalidDataException("Task route has no map.");
        string actText = match.Success ? Regex.Replace(match.Value, @"\s+", " ") : "Act 1";
        StoryAct act = actText.ToLowerInvariant() switch
        {
            "act 1" => StoryAct.Act1,
            "act 2" => StoryAct.Act2,
            "act 3" => StoryAct.Act3,
            "act 4" => StoryAct.Act4,
            "act 5" => StoryAct.Act5,
            "infinite" => StoryAct.Infinite,
            "mastery" => StoryAct.Mastery,
            _ => throw new InvalidDataException("Task route act is invalid."),
        };
        return (map, act);
    }

    private static (string Map, StoryAct Act) ParseLegacyRoute(string route)
    {
        string[] parts = route.Replace("Â·", "·", StringComparison.Ordinal)
            .Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string map = parts.FirstOrDefault() ?? throw new InvalidDataException("Task route has no map.");
        string actText = parts.FirstOrDefault(part => part.StartsWith("Act ", StringComparison.OrdinalIgnoreCase))
            ?? parts.FirstOrDefault(part => part.Equals("Infinite", StringComparison.OrdinalIgnoreCase) || part.Equals("Mastery", StringComparison.OrdinalIgnoreCase))
            ?? "Act 1";
        StoryAct act = actText.ToLowerInvariant() switch
        {
            "act 1" => StoryAct.Act1,
            "act 2" => StoryAct.Act2,
            "act 3" => StoryAct.Act3,
            "act 4" => StoryAct.Act4,
            "act 5" => StoryAct.Act5,
            "infinite" => StoryAct.Infinite,
            "mastery" => StoryAct.Mastery,
            _ => throw new InvalidDataException("Task route act is invalid."),
        };
        return (map, act);
    }

    private static int? OptionalKey(IReadOnlyDictionary<string, int?> keys, string name) =>
        keys.TryGetValue(name, out int? value) ? value : null;

    private static int RequiredKey(IReadOnlyDictionary<string, int?> keys, string name) =>
        OptionalKey(keys, name) ?? throw new InvalidDataException($"Runner key binding {name} is missing.");

    private static string SelectDevice(OcrRunner ocr, bool preferGpu) =>
        preferGpu && ocr.IsDeviceReady(OcrRunner.GpuDevice) ? OcrRunner.GpuDevice : OcrRunner.CpuDevice;

    private static string RouteId(StoryAct act) => act switch
    {
        StoryAct.Act1 => "act-1",
        StoryAct.Act2 => "act-2",
        StoryAct.Act3 => "act-3",
        StoryAct.Act4 => "act-4",
        StoryAct.Act5 => "act-5",
        StoryAct.Infinite => "infinite",
        StoryAct.Mastery => "mastery",
        _ => throw new ArgumentOutOfRangeException(nameof(act)),
    };

    private static string Slug(string value) => value.ToLowerInvariant().Replace("'", string.Empty).Replace(' ', '-');
    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    private static void Report(
        IProgress<SessionRuntimeProgress> progress,
        RunnerTaskSnapshot task,
        string stage,
        IReadOnlyDictionary<string, int> wins,
        IReadOnlyDictionary<string, int> losses,
        string detail) => progress.Report(new SessionRuntimeProgress
        {
            TaskId = task.Id,
            Stage = stage,
            Wins = wins.Values.Sum(),
            Losses = losses.Values.Sum(),
            Detail = detail,
        });

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
