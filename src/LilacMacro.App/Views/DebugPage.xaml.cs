using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LilacMacro.App.Debugging;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Views;

public partial class DebugPage : UserControl, IWorkspacePage
{
    private readonly WorkspaceController _workspace;
    private readonly OcrRunner _ocr;
    private readonly DebugOcrController _debug;
    private readonly DebugEvidenceRunService _evidence;
    private readonly DebugKeySequenceCoordinator _debugInput;
    private readonly List<string> _events = [];
    private string _device;
    private DebugEvidenceMode _evidenceMode = DebugEvidenceMode.Ocr;
    private StoryDifficulty _difficulty = StoryDifficulty.Normal;
    private int _expeditionDifficulty = 1;
    private bool _busy;
    private readonly DeepDebugSessionService _deepDebug;

    internal DebugPage(
        WorkspaceController workspace,
        OcrRunner ocr,
        DebugKeySequenceCoordinator debugInput,
        DeepDebugSessionService deepDebug, string defaultOcrDevice)
    {
        _workspace = workspace;
        _ocr = ocr;
        _debug = new DebugOcrController(workspace, ocr);
        _evidence = new DebugEvidenceRunService(workspace, deepDebug);
        _debugInput = debugInput;
        _deepDebug = deepDebug;
        _device = defaultOcrDevice;
        InitializeComponent();
        KeyChainControl.Initialize(debugInput);
        _debugInput.Changed += DebugInput_OnChanged;
        UpdateDifficultyButtons();
        UpdateExpeditionDifficultyButtons();
    }
    public Task RefreshAsync()
    {
        KeepLoadedToggle.IsChecked = _ocr.KeepLoaded;
        UpdateControls();
        return Task.CompletedTask;
    }

    private async Task RunAsync(
        Func<Task<DebugRunReport>> operation,
        DebugStateSpec? imageState = null)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            StatusText.Text = "RUNNING";
            StatusBand.SetResourceReference(Border.BackgroundProperty, "YellowBrush");
            OcrMetaText.Text = _evidenceMode == DebugEvidenceMode.Ocr
                ? $"OCR | PP-OCRv6 SMALL | {_device.ToUpperInvariant()}"
                : $"IMAGE FIRST | OCR FALLBACK {_device.ToUpperInvariant()}";
            DebugEvidenceRunResult result = await _deepDebug.RunOperationAsync(
                "debug-action",
                new DeepDebugOperationContext(
                    "runtime-lab",
                    new { Device = _device, EvidenceMode = _evidenceMode.ToString(), State = imageState?.Name }),
                async token =>
                {
                    DebugEvidenceRunResult evidence = await _evidence.RunAsync(
                        _evidenceMode,
                        imageState,
                        operation,
                        token);
                    WireDebugEvidence.RecordComparisons(_deepDebug, evidence.Comparisons);
                    _deepDebug.RecordEvent("debug", "report", new
                    {
                        evidence.Succeeded,
                        evidence.Status,
                        evidence.Events,
                        evidence.ImageResult,
                        evidence.Comparisons,
                        Ocr = evidence.OcrReport is null
                            ? null
                            : WireDebugEvidence.Snapshot(evidence.OcrReport.Snapshot),
                    });
                    return evidence;
                },
                CancellationToken.None);
            ShowResult(result);
        }
        catch (Exception error)
        {
            ShowError(error.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowResult(DebugEvidenceRunResult result)
    {
        StatusText.Text = result.Status;
        StatusBand.SetResourceReference(
            Border.BackgroundProperty,
            result.Succeeded ? "SuccessBrush" : "DangerBrush");
        MatchCountText.Text = result.MatchCount;
        OcrMetaText.Text = result.Meta;
        EvidenceList.ItemsSource = result.Rows;
        foreach (string entry in result.Events) AddEvent(entry);
    }

    private void ShowError(string message)
    {
        StatusText.Text = "ERROR";
        MatchCountText.Text = "0/0";
        StatusBand.SetResourceReference(Border.BackgroundProperty, "DangerBrush");
        OcrMetaText.Text = message;
        AddEvent($"ERROR {message}");
    }

    private void AddEvent(string entry)
    {
        _events.Insert(0, $"{DateTime.Now:HH:mm:ss}  {entry}");
        if (_events.Count > 50) _events.RemoveRange(50, _events.Count - 50);
        EventLog.ItemsSource = null;
        EventLog.ItemsSource = _events;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateControls();
    }

    private void UpdateControls()
    {
        bool ready = _ocr.IsDeviceReady(_device);
        bool inputIdle = _debugInput.State == DebugKeySequenceState.Idle;
        ActionPanel.IsEnabled = ready && !_busy && inputIdle;
        CameraAlignButton.IsEnabled = !_busy && inputIdle;
        KeyChainControl.SetHostBusy(_busy);
        PrepareButton.IsEnabled = !_busy;
        EvidenceModeBox.IsEnabled = !_busy;
        DeviceToggle.IsEnabled = !_busy;
        KeepLoadedToggle.IsEnabled = _ocr.IsInstalled && !_busy;
        SetupButton.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        SetupButton.IsEnabled = !_busy;
        SetupButton.Content = _device == OcrRunner.GpuDevice ? "SET UP GPU" : "SET UP OCR";
        DeviceToggle.Content = _device == OcrRunner.GpuDevice ? "GPU" : "CPU";
        DeviceToggle.SetResourceReference(
            Control.BackgroundProperty,
            _device == OcrRunner.GpuDevice ? "AccentBrush" : "CardBrush");
        DeviceToggle.SetResourceReference(
            Control.ForegroundProperty,
            _device == OcrRunner.GpuDevice ? "CardBrush" : "InkBrush");
    }

    private void DebugInput_OnChanged(object? sender, EventArgs eventArgs)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(UpdateControls);
            return;
        }
        UpdateControls();
    }

    private async void CameraAlign_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_busy || _debugInput.State != DebugKeySequenceState.Idle) return;
        SetBusy(true);
        try
        {
            StatusText.Text = "ALIGNING CAMERA";
            StatusBand.SetResourceReference(Border.BackgroundProperty, "YellowBrush");
            await _workspace.AlignCameraAsync(DebugWorkflowCatalog.ClientSize);
            StatusText.Text = "CAMERA ALIGNED";
            StatusBand.SetResourceReference(Border.BackgroundProperty, "SuccessBrush");
            AddEvent("CAMERA ALIGNED");
        }
        catch (Exception error)
        {
            ShowError(error.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Prepare_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            await _debug.PrepareAsync();
            StatusText.Text = "ROBLOX 1366 x 700";
            StatusBand.SetResourceReference(Border.BackgroundProperty, "SuccessBrush");
            AddEvent("SIZE 1366 x 700");
        }
        catch (Exception error)
        {
            ShowError(error.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void DeviceToggle_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_busy) return;
        _device = _device == OcrRunner.CpuDevice ? OcrRunner.GpuDevice : OcrRunner.CpuDevice;
        UpdateControls();
        AddEvent($"DEVICE {_device.ToUpperInvariant()}");
    }

    private void EvidenceMode_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs) =>
        _evidenceMode = EvidenceModeBox.SelectedIndex == 0
            ? DebugEvidenceMode.Ocr
            : DebugEvidenceMode.ImageWithOcrFallback;

    private void KeepLoaded_OnChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (!IsLoaded || _busy) return;
        _ocr.KeepLoaded = KeepLoadedToggle.IsChecked == true;
        AddEvent(_ocr.KeepLoaded ? "KEEP LOADED ON" : "KEEP LOADED OFF");
    }

    private async void Setup_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            StatusText.Text = "SETTING UP OCR";
            StatusBand.SetResourceReference(Border.BackgroundProperty, "YellowBrush");
            await _debug.SetupAsync(_device);
            StatusText.Text = $"OCR {_device.ToUpperInvariant()} READY";
            StatusBand.SetResourceReference(Border.BackgroundProperty, "SuccessBrush");
            AddEvent($"OCR {_device.ToUpperInvariant()} READY");
        }
        catch (Exception error)
        {
            ShowError(error.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void CheckLobby_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.CheckLobbyAsync(_device), DebugWorkflowCatalog.Lobby);
    private async void OpenPlay_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.OpenPlayAsync(_device));
    private async void OpenUnits_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.OpenUnitsAsync(_device));
    private async void OpenEvents_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.OpenEventsAsync(_device));
    private async void OpenAreas_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.OpenAreasAsync(_device));

    private async void CheckEvents_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.CheckEventsAsync(_device), DebugWorkflowCatalog.EventSelect);

    private async void VillainInvasion_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectEventAsync(EventDestination.VillainInvasion);

    private async void BossBounty_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectEventAsync(EventDestination.BossBounty);

    private async void GuessThatUnit_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectEventAsync(EventDestination.GuessThatUnit);

    private Task SelectEventAsync(EventDestination destination) =>
        RunAsync(() => _debug.SelectEventAsync(destination, _device));

    private async void CheckAreas_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.CheckAreasAsync(_device), DebugWorkflowCatalog.AreasUi);

    private async void UpgradeArea_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectAreaAsync(AreaCategory.Upgrade);

    private async void GamemodeArea_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectAreaAsync(AreaCategory.Gamemode);

    private async void LobbyArea_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectAreaAsync(AreaCategory.Lobby);

    private async void ShopArea_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectAreaAsync(AreaCategory.Shop);

    private async void ExpeditionArea_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectAreaAsync(AreaCategory.Expedition);

    private Task SelectAreaAsync(AreaCategory category) =>
        RunAsync(() => _debug.SelectAreaAsync(category, _device));

    private async void CheckPlayUi_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.CheckPlayUiAsync(_device), DebugWorkflowCatalog.PlayUi);

    private async void Story_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectModeAsync("Story");

    private async void Raid_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectModeAsync("Raid");

    private async void Challenge_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectModeAsync("Challenge");

    private async void Expedition_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectModeAsync("Expedition");

    private async void Tower_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectModeAsync("Tower");

    private Task SelectModeAsync(string mode) => RunAsync(() => _debug.SelectModeAsync(mode, _device));

    private async void CheckUnitInventory_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.CheckUnitInventoryAsync(_device), DebugWorkflowCatalog.UnitInventory);

    private async void OpenTeams_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.OpenTeamsAsync(_device));

    private async void CheckTeamSwap_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.CheckTeamSwapAsync(_device), DebugWorkflowCatalog.TeamSwap);

    private async void Team1_OnClick(object sender, RoutedEventArgs eventArgs) => await LoadTeamAsync(1);

    private async void Team2_OnClick(object sender, RoutedEventArgs eventArgs) => await LoadTeamAsync(2);

    private async void Team3_OnClick(object sender, RoutedEventArgs eventArgs) => await LoadTeamAsync(3);

    private async void Team4_OnClick(object sender, RoutedEventArgs eventArgs) => await LoadTeamAsync(4);

    private async void Team5_OnClick(object sender, RoutedEventArgs eventArgs) => await LoadTeamAsync(5);

    private async void Team6_OnClick(object sender, RoutedEventArgs eventArgs) => await LoadTeamAsync(6);

    private async void Team7_OnClick(object sender, RoutedEventArgs eventArgs) => await LoadTeamAsync(7);

    private async void Team8_OnClick(object sender, RoutedEventArgs eventArgs) => await LoadTeamAsync(8);

    private Task LoadTeamAsync(int teamNumber) =>
        RunAsync(() => _debug.LoadTeamAsync(teamNumber, _device));

    private async void CheckChallengeTypes_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.CheckChallengeTypesAsync(_device), DebugWorkflowCatalog.ChallengeTypePicker);

    private async void TraitChallenge_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectChallengeTypeAsync(RegularChallengeType.Trait);

    private async void StatChallenge_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectChallengeTypeAsync(RegularChallengeType.Stat);

    private async void SpriteChallenge_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectChallengeTypeAsync(RegularChallengeType.Sprite);

    private Task SelectChallengeTypeAsync(RegularChallengeType type) =>
        RunAsync(() => _debug.SelectChallengeTypeAsync(type, _device));

    private async void CheckMaps_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.CheckMapsAsync(_device), DebugWorkflowCatalog.StoryMap);

    private async void School_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectMapAsync("School Grounds");

    private async void Flower_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectMapAsync("Flower Forest");

    private async void Rose_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectMapAsync("Rose Kingdom");

    private async void FairyKing_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectMapAsync("Fairy King Forest");

    private async void KingsTomb_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectMapAsync("King's Tomb");

    private async void EastTown_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectMapAsync("East Town");

    private Task SelectMapAsync(string map) => RunAsync(() => _debug.SelectMapAsync(map, _device));

    private async void CheckRaidMaps_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.CheckRaidMapsAsync(_device), DebugWorkflowCatalog.RaidMap);

    private async void SpiritCity_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.SelectRaidMapAsync("Spirit City", _device));

    private async void CheckExpeditionMaps_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.CheckExpeditionMapsAsync(_device), DebugWorkflowCatalog.ExpeditionMap);

    private void ExpeditionDifficulty1_OnClick(object sender, RoutedEventArgs eventArgs) =>
        SetExpeditionDifficulty(1);

    private void ExpeditionDifficulty2_OnClick(object sender, RoutedEventArgs eventArgs) =>
        SetExpeditionDifficulty(2);

    private void ExpeditionDifficulty3_OnClick(object sender, RoutedEventArgs eventArgs) =>
        SetExpeditionDifficulty(3);

    private async void ExpeditionSchool_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectExpeditionMapAsync("School Grounds");

    private async void ExpeditionFlower_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectExpeditionMapAsync("Flower Forest");

    private async void ExpeditionRose_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectExpeditionMapAsync("Rose Kingdom");

    private async void ExpeditionEastTown_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectExpeditionMapAsync("East Town");

    private Task SelectExpeditionMapAsync(string map) => RunAsync(
        () => _debug.SelectExpeditionMapAsync(map, _expeditionDifficulty, _device));

    private void SetExpeditionDifficulty(int difficulty)
    {
        if (_busy || _expeditionDifficulty == difficulty) return;
        _expeditionDifficulty = difficulty;
        UpdateExpeditionDifficultyButtons();
        AddEvent($"EXPEDITION DIFFICULTY {difficulty}");
    }

    private void UpdateExpeditionDifficultyButtons()
    {
        ExpeditionDifficulty1Button.Style = (Style)FindResource(
            _expeditionDifficulty == 1 ? "PrimaryButtonStyle" : "ButtonStyle");
        ExpeditionDifficulty2Button.Style = (Style)FindResource(
            _expeditionDifficulty == 2 ? "PrimaryButtonStyle" : "ButtonStyle");
        ExpeditionDifficulty3Button.Style = (Style)FindResource(
            _expeditionDifficulty == 3 ? "PrimaryButtonStyle" : "ButtonStyle");
    }

    private async void CheckRaidActs_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.CheckRaidActsAsync(_device), DebugWorkflowCatalog.RaidActPicker);

    private async void RaidAct1_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectRaidActAsync(StoryAct.Act1);

    private async void RaidAct2_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectRaidActAsync(StoryAct.Act2);

    private async void RaidAct3_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await SelectRaidActAsync(StoryAct.Act3);

    private Task SelectRaidActAsync(StoryAct act) =>
        RunAsync(() => _debug.SelectRaidActAsync(act, _device));

    private async void CheckActs_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.CheckActsAsync(_device), DebugWorkflowCatalog.StoryActPicker);

    private void NormalDifficulty_OnClick(object sender, RoutedEventArgs eventArgs) =>
        SetDifficulty(StoryDifficulty.Normal);

    private void HardDifficulty_OnClick(object sender, RoutedEventArgs eventArgs) =>
        SetDifficulty(StoryDifficulty.Hard);

    private async void Act1_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectActAsync(StoryAct.Act1);

    private async void Act2_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectActAsync(StoryAct.Act2);

    private async void Act3_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectActAsync(StoryAct.Act3);

    private async void Act4_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectActAsync(StoryAct.Act4);

    private async void Act5_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectActAsync(StoryAct.Act5);

    private async void Infinite_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectActAsync(StoryAct.Infinite);

    private async void Mastery_OnClick(object sender, RoutedEventArgs eventArgs) => await SelectActAsync(StoryAct.Mastery);

    private Task SelectActAsync(StoryAct act) => RunAsync(() => _debug.SelectActAsync(act, _difficulty, _device));

    private void SetDifficulty(StoryDifficulty difficulty)
    {
        if (_busy || _difficulty == difficulty) return;
        _difficulty = difficulty;
        UpdateDifficultyButtons();
        AddEvent($"DIFFICULTY {difficulty.ToString().ToUpperInvariant()}");
    }

    private void UpdateDifficultyButtons()
    {
        NormalDifficultyButton.Style = (Style)FindResource(
            _difficulty == StoryDifficulty.Normal ? "PrimaryButtonStyle" : "ButtonStyle");
        HardDifficultyButton.Style = (Style)FindResource(
            _difficulty == StoryDifficulty.Hard ? "PrimaryButtonStyle" : "ButtonStyle");
    }

    private async void CheckMatchPreview_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.CheckMatchPreviewAsync(_device), DebugWorkflowCatalog.MatchPreview);

    private async void StartMatch_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.StartMatchAsync(_device));

    private async void CheckMatchPrestart_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.CheckMatchPrestartAsync(_device), DebugWorkflowCatalog.MatchPrestart);

    private async void StartGame_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.StartGameAsync(_device));

    private async void CheckDefeat_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.CheckDefeatAsync(_device), DebugWorkflowCatalog.Defeat);

    private async void RepeatStage_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.RepeatStageAsync(_device));

    private async void CheckVictory_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.CheckVictoryAsync(_device), DebugWorkflowCatalog.Victory);

    private async void RepeatVictoryStage_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(() => _debug.RepeatVictoryStageAsync(_device));
}
