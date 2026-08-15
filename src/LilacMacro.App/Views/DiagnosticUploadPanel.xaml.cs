using Microsoft.Win32;
using System.Net.Http;
using System.Windows;
using LilacMacro.App.Notifications;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Services;
using LilacMacro.Runtime.Services;

namespace LilacMacro.App.Views;

public partial class DiagnosticUploadPanel
{
    private readonly MacroOwnerState _ownerState;
    private readonly DiagnosticInstallationStore _installation;
    private readonly IDiagnosticUploadTransport _transport;
    private CancellationTokenSource? _uploadCancellation;
    private bool _initialized;
    private bool _installationLoaded;

    internal DiagnosticUploadPanel(
        MacroOwnerState ownerState,
        DiagnosticInstallationStore installation,
        IDiagnosticUploadTransport transport)
    {
        _ownerState = ownerState;
        _installation = installation;
        _transport = transport;
        InitializeComponent();
        AllowUploadsCheck.IsChecked = ownerState.EnableDiagnosticUploads;
        RefreshAvailability();
        _initialized = true;
    }

    internal void Cancel() => _uploadCancellation?.Cancel();

    private async void DiagnosticUploadPanel_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_installationLoaded) return;
        _installationLoaded = true;
        try
        {
            Guid installId = await _installation.GetOrCreateAsync(CancellationToken.None);
            InstallationIdText.Text = installId.ToString("D");
            CopyInstallationIdButton.IsEnabled = true;
        }
        catch (Exception exception) when (exception is
            InvalidDataException or IOException or UnauthorizedAccessException)
        {
            InstallationIdText.Text = "Unavailable";
            AppToastService.ShowError(
                "INSTALLATION ID UNAVAILABLE",
                "LilacMacro could not load the diagnostic installation identity.");
        }
    }

    private void CopyInstallationId_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (!Guid.TryParse(InstallationIdText.Text, out _)) return;
        try
        {
            Clipboard.SetText(InstallationIdText.Text);
            AppToastService.ShowSuccess(
                "INSTALLATION ID COPIED",
                "Send this ID and the archive's exact byte size to a LilacMacro administrator.");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            AppToastService.ShowError(
                "CLIPBOARD UNAVAILABLE",
                "Select and copy the installation ID manually.");
        }
    }

    private void AllowUploads_OnChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (!_initialized) return;
        _ownerState.SetDiagnosticUploadConsent(AllowUploadsCheck.IsChecked == true);
        RefreshAvailability();
    }

    private async void Upload_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_uploadCancellation is not null || !_ownerState.EnableDiagnosticUploads) return;
        OpenFileDialog dialog = new()
        {
            CheckFileExists = true,
            Filter = "Diagnostic ZIP (*.zip)|*.zip",
            Multiselect = false,
            Title = "Select diagnostic archive",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        _uploadCancellation = new CancellationTokenSource();
        RefreshAvailability();
        try
        {
            Guid installId = await _installation.GetOrCreateAsync(_uploadCancellation.Token);
            Progress<DiagnosticUploadProgress> progress = new(UpdateProgress);
            DiagnosticUploadResult result = await _transport.UploadAsync(
                dialog.FileName,
                InferKind(dialog.SafeFileName),
                BuildVersion(),
                installId,
                LargeGrantPassword.Password,
                progress,
                _uploadCancellation.Token);
            StatusText.Text = $"Queued {result.UploadId.ToString("N")[..8]} · {result.Status}";
            AppToastService.ShowSuccess(
                "DIAGNOSTIC UPLOAD QUEUED",
                "The selected archive is awaiting server verification.");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Upload canceled";
        }
        catch (Exception exception) when (exception is
            InvalidDataException or IOException or UnauthorizedAccessException or
            HttpRequestException or System.Text.Json.JsonException)
        {
            string message = SafeFailure(exception);
            StatusText.Text = message;
            AppToastService.ShowError("DIAGNOSTIC UPLOAD FAILED", message);
        }
        finally
        {
            LargeGrantPassword.Clear();
            _uploadCancellation.Dispose();
            _uploadCancellation = null;
            RefreshAvailability();
        }
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs eventArgs) => Cancel();

    private void RefreshAvailability()
    {
        bool enabled = AllowUploadsCheck.IsChecked == true;
        bool idle = _uploadCancellation is null;
        UploadButton.IsEnabled = enabled && idle;
        LargeGrantPassword.IsEnabled = enabled && idle;
        CancelButton.IsEnabled = !idle;
        if (!enabled && idle) StatusText.Text = "Opt-in disabled";
        else if (enabled && idle && StatusText.Text == "Opt-in disabled")
            StatusText.Text = "Ready for explicit ZIP selection";
    }

    private void UpdateProgress(DiagnosticUploadProgress progress)
    {
        UploadProgress.Value = progress.TotalBytes > 0
            ? Math.Clamp(progress.BytesCompleted * 100d / progress.TotalBytes, 0, 100)
            : 0;
        StatusText.Text = progress.Phase switch
        {
            DiagnosticUploadPhase.Preparing => "Preparing archive",
            DiagnosticUploadPhase.Hashing => $"Hashing · {UploadProgress.Value:0}%",
            DiagnosticUploadPhase.Uploading when progress.PartNumber is int part =>
                $"Uploading part {part}/{progress.PartCount} · {UploadProgress.Value:0}%",
            DiagnosticUploadPhase.Uploading => $"Uploading · {UploadProgress.Value:0}%",
            DiagnosticUploadPhase.Finalizing => "Finalizing upload",
            DiagnosticUploadPhase.Complete => "Upload complete",
            _ => "Working",
        };
    }

    private static DiagnosticArchiveKind InferKind(string fileName)
    {
        if (fileName.Contains("installer", StringComparison.OrdinalIgnoreCase))
            return DiagnosticArchiveKind.InstallerLog;
        if (fileName.Contains("live-debug", StringComparison.OrdinalIgnoreCase))
            return DiagnosticArchiveKind.LiveDebug;
        if (fileName.Contains("runtime-log", StringComparison.OrdinalIgnoreCase))
            return DiagnosticArchiveKind.RuntimeLog;
        return DiagnosticArchiveKind.DeepDebug;
    }

    private static string BuildVersion() =>
        typeof(DiagnosticUploadPanel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static string SafeFailure(Exception exception) => exception switch
    {
        FileNotFoundException => "The selected archive is no longer available.",
        UnauthorizedAccessException => "The selected archive could not be read.",
        IOException => "The selected archive could not be read.",
        HttpRequestException => "The diagnostic service or storage transfer failed.",
        _ => exception.Message,
    };
}
