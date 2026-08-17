using System.Globalization;
using System.Text;
using System.Windows;
using LilacMacro.App.Infrastructure;

namespace LilacMacro.App.Views;

public partial class GpuOcrSetupWindow : Window
{
    private readonly OcrRunner _ocr;
    private readonly OcrGpuInfo _gpu;
    private CancellationTokenSource _cancellation = new();
    private bool _busy;
    private bool _closeRequested;
    private bool _started;
    private readonly StringBuilder _log = new();

    internal GpuOcrSetupWindow(OcrRunner ocr, OcrGpuInfo gpu)
    {
        _ocr = ocr;
        _gpu = gpu;
        InitializeComponent();
        GpuDetailsText.Text =
            $"{gpu.Name}  |  {gpu.Generation}  |  compute {gpu.ComputeCapability.ToString("0.0", CultureInfo.InvariantCulture)}  |  CUDA {gpu.CudaFeed}  |  driver {gpu.DriverVersion}";
        Loaded += Window_OnLoaded;
        Closing += Window_OnClosing;
        Closed += (_, _) => _cancellation.Dispose();
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_started) return;
        _started = true;
        await RunSetupAsync();
    }

    private async void Retry_OnClick(object sender, RoutedEventArgs eventArgs) => await RunSetupAsync();

    private async Task RunSetupAsync()
    {
        if (_busy) return;
        _busy = true;
        _closeRequested = false;
        _cancellation.Dispose();
        _cancellation = new CancellationTokenSource();
        SetupProgress.Value = 0;
        RetryButton.Visibility = Visibility.Collapsed;
        CancelButton.IsEnabled = true;
        ContinueButton.IsEnabled = false;
        ContinueButton.Content = "CONTINUE WITH CPU";
        StatusText.Text = "SETTING UP GPU OCR";
        ProgressText.Text = "Starting...";
        AppendLog("LilacMacro GPU OCR setup started.");
        try
        {
            Progress<string> progress = new(UpdateProgress);
            await _ocr.SetupAsync(OcrRunner.GpuDevice, _cancellation.Token, progress);
            SetupProgress.Value = 100;
            StatusText.Text = "GPU OCR READY";
            ProgressText.Text = "GPU OCR is ready.";
            ContinueButton.Content = "CONTINUE";
            AppendLog("GPU OCR setup completed successfully.");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "SETUP CANCELED";
            ProgressText.Text = "CPU OCR remains available.";
            ContinueButton.Content = "CONTINUE WITH CPU";
            AppendLog("GPU OCR setup canceled. CPU OCR remains available.");
        }
        catch (Exception error)
        {
            StatusText.Text = "GPU SETUP FAILED";
            ProgressText.Text = "CPU OCR remains available.";
            ContinueButton.Content = "CONTINUE WITH CPU";
            AppendLog($"GPU OCR setup failed: {error.Message}");
        }
        finally
        {
            _busy = false;
            CancelButton.IsEnabled = false;
            ContinueButton.IsEnabled = true;
            RetryButton.Visibility = Visibility.Visible;
            if (_closeRequested) DialogResult = true;
        }
    }

    private void UpdateProgress(string entry)
    {
        const string prefix = "[OCR_STAGE] ";
        if (entry.StartsWith(prefix, StringComparison.Ordinal))
        {
            string value = entry[prefix.Length..];
            int separator = value.IndexOf('|');
            if (separator > 0 && int.TryParse(value[..separator], NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent))
            {
                SetupProgress.Value = Math.Clamp(percent, 0, 100);
                string message = value[(separator + 1)..];
                ProgressText.Text = message;
                AppendLog(message);
                return;
            }
        }
        AppendLog(entry);
    }

    private void AppendLog(string entry)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => AppendLog(entry));
            return;
        }
        if (_log.Length > 18000) _log.Remove(0, _log.Length - 16000);
        if (_log.Length > 0) _log.AppendLine();
        _log.Append($"{DateTime.Now:HH:mm:ss}  {entry}");
        LogText.Text = _log.ToString();
        LogText.ScrollToEnd();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_busy)
        {
            _closeRequested = false;
            CancelButton.IsEnabled = false;
            StatusText.Text = "CANCELING GPU SETUP";
            _cancellation.Cancel();
            return;
        }
        DialogResult = true;
    }

    private void Continue_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_busy) return;
        DialogResult = true;
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs eventArgs)
    {
        if (!_busy) return;
        eventArgs.Cancel = true;
        _closeRequested = true;
        _cancellation.Cancel();
    }
}
