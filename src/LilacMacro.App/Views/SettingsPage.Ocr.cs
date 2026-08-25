using System.IO;
using System.Windows;
using System.Windows.Controls;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Notifications;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Views;

public partial class SettingsPage
{
    private bool _ocrBusy;

    private void InitializeOcrControls()
    {
        OcrModeCombo.ItemsSource = OcrModeOption.All;
        OcrModeCombo.SelectedItem = OcrModeOption.All.First(option => option.Mode == _ownerState.OcrMode);
        RefreshOcrControls();
    }

    private void OcrMode_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_initialized || OcrModeCombo.SelectedItem is not OcrModeOption option) return;
        _ownerState.SetOcrMode(option.Mode);
        RefreshOcrControls();
        AppToastService.ShowSuccess("OCR MODE SAVED", option.Name);
    }

    private async void TestOcr_OnClick(object sender, RoutedEventArgs eventArgs)
        => await RunOcrTestAsync();

    private async void RepairOcr_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_ocrBusy) return;
        SetOcrBusy(true);
        try
        {
            Progress<string> progress = new(message => OcrRuntimeText.Text = message);
            string device = await _ocr.RepairAsync(_ownerState.OcrMode, progress: progress);
            AppToastService.ShowSuccess("OCR REPAIRED", $"{DeviceName(device)} runtime is ready.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppToastService.ShowError("OCR REPAIR FAILED", exception.Message);
        }
        finally
        {
            SetOcrBusy(false);
        }
    }

    private async Task RunOcrTestAsync()
    {
        if (_ocrBusy) return;
        SetOcrBusy(true);
        string testRoot = Path.Combine(Path.GetTempPath(), "LilacMacro", "ocr-self-test");
        string imagePath = Path.Combine(testRoot, $"{Guid.NewGuid():N}.png");
        try
        {
            Directory.CreateDirectory(testRoot);
            OcrSelfTestImage.Write(imagePath);
            OcrRuntimeText.Text = "Starting selected OCR runtime...";
            string device = await _ocr.EnsureReadyAsync(_ownerState.OcrMode);
            OcrRuntimeText.Text = $"Testing {DeviceName(device)} OCR...";
            OcrWorkerResult result = await _ocr.RunAsync(
                imagePath,
                new PixelRect(0, 0, OcrSelfTestImage.Width, OcrSelfTestImage.Height),
                OcrRunner.SmallModel,
                device);
            bool recognized = Normalize(result.Text).Contains("test", StringComparison.Ordinal);
            if (!recognized)
                throw new InvalidOperationException($"Expected TEST; OCR returned {DisplayResult(result.Text)}.");

            AppToastService.ShowSuccess(
                "OCR TEST PASSED",
                $"{DeviceName(result.Device)} · {result.InferenceMilliseconds} ms · {result.Confidence:P0} confidence");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppToastService.ShowError("OCR TEST FAILED", exception.Message);
        }
        finally
        {
            try { File.Delete(imagePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            SetOcrBusy(false);
        }
    }

    private void SetOcrBusy(bool busy)
    {
        _ocrBusy = busy;
        OcrModeCombo.IsEnabled = !busy;
        TestOcrButton.IsEnabled = !busy;
        RepairOcrButton.IsEnabled = !busy;
        if (!busy) RefreshOcrControls();
    }

    private void RefreshOcrControls()
    {
        OcrModeOption selected = OcrModeOption.All.First(option => option.Mode == _ownerState.OcrMode);
        OcrModeDescriptionText.Text = selected.Description;
        string cpu = _ocr.IsDeviceReady(OcrRunner.CpuDevice) ? "CPU ready" : "CPU not ready";
        string gpu = _ocr.IsDeviceReady(OcrRunner.GpuDevice) ? "GPU ready" : "GPU not set up";
        OcrRuntimeText.Text = $"{cpu} · {gpu}";
    }

    private static string Normalize(string text) =>
        new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string DisplayResult(string text) =>
        string.IsNullOrWhiteSpace(text) ? "no text" : $"“{text.Trim()}”";

    private static string DeviceName(string device) =>
        device.StartsWith("gpu", StringComparison.OrdinalIgnoreCase) ? "GPU" : "CPU";

    private sealed record OcrModeOption(OcrExecutionMode Mode, string Name, string Description)
    {
        internal static readonly OcrModeOption[] All =
        [
            new(OcrExecutionMode.Automatic, "Auto", "Uses GPU when ready and otherwise uses the bundled CPU runtime."),
            new(OcrExecutionMode.GpuPreferred, "GPU", "Uses the NVIDIA GPU runtime. Repair installs or restores it when needed."),
            new(OcrExecutionMode.CpuOnly, "CPU", "Always uses the bundled CPU runtime."),
        ];
    }
}
