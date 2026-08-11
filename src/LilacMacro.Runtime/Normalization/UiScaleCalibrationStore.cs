using System.Diagnostics;
using System.Text.Json;

namespace LilacMacro.Runtime.Normalization;

internal sealed class UiScaleCalibrationStore
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private readonly string _path;
    private readonly string _sessionKey;

    public UiScaleCalibrationStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LilacMacro"),
            Process.GetCurrentProcess().SessionId)
    {
    }

    internal UiScaleCalibrationStore(string appDataRoot, int sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        if (sessionId < 0) throw new ArgumentOutOfRangeException(nameof(sessionId));
        _path = Path.Combine(Path.GetFullPath(appDataRoot), "ui-scale-calibration.json");
        _sessionKey = $"windows-session-{sessionId}";
    }

    public async Task<double?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;
        try
        {
            await using FileStream stream = File.OpenRead(_path);
            CalibrationDocument? document = await JsonSerializer.DeserializeAsync<CalibrationDocument>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (document?.Version != SchemaVersion ||
                !document.Sessions.TryGetValue(_sessionKey, out CalibrationEntry? entry) ||
                !IsSupported(entry.Value))
            {
                return null;
            }
            return entry.Value;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(double value, CancellationToken cancellationToken = default)
    {
        if (!IsSupported(value)) throw new ArgumentOutOfRangeException(nameof(value));

        CalibrationDocument document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
        document.Sessions[_sessionKey] = new CalibrationEntry
        {
            Value = value,
            UpdatedUtc = DateTimeOffset.UtcNow,
        };
        string? directory = Path.GetDirectoryName(_path);
        if (directory is null) return;
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
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task<CalibrationDocument> LoadDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new CalibrationDocument();
        try
        {
            await using FileStream stream = File.OpenRead(_path);
            CalibrationDocument? document = await JsonSerializer.DeserializeAsync<CalibrationDocument>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return document?.Version == SchemaVersion ? document : new CalibrationDocument();
        }
        catch (IOException)
        {
            return new CalibrationDocument();
        }
        catch (UnauthorizedAccessException)
        {
            return new CalibrationDocument();
        }
        catch (JsonException)
        {
            return new CalibrationDocument();
        }
    }

    private static bool IsSupported(double value) =>
        double.IsFinite(value) &&
        value is >= UiScaleFeedbackPolicy.MinimumValue and <= UiScaleFeedbackPolicy.MaximumValue;

    private sealed class CalibrationDocument
    {
        public int Version { get; init; } = SchemaVersion;
        public Dictionary<string, CalibrationEntry> Sessions { get; init; } = new(StringComparer.Ordinal);
    }

    private sealed class CalibrationEntry
    {
        public double Value { get; init; }
        public DateTimeOffset UpdatedUtc { get; init; }
    }
}
