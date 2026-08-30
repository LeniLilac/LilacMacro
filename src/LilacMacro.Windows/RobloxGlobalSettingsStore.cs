using System.Text;
using System.Xml;
using System.Xml.Linq;
using LilacMacro.Core.Roblox;

namespace LilacMacro.Windows;

internal sealed class RobloxGlobalSettingsStore(string? settingsPath = null)
{
    private const long MaximumSettingsBytes = 1024 * 1024;
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly string _settingsPath = settingsPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox",
        "GlobalBasicSettings_13.xml");

    public async Task<RobloxSettingsNormalizationResult> NormalizeAsync(
        CancellationToken cancellationToken = default)
    {
        RecoverInterruptedReplacement();
        XDocument document = await ReadAsync(_settingsPath, cancellationToken).ConfigureAwait(false);
        RobloxSettingsNormalizationResult result = RobloxGlobalSettingsPolicy.Normalize(document);
        if (!result.Changed) return result;

        string temporaryPath = $"{_settingsPath}.lilacmacro-{Guid.NewGuid():N}.tmp";
        string backupPath = BackupPath;
        try
        {
            await WriteAsync(temporaryPath, document, cancellationToken).ConfigureAwait(false);
            XDocument staged = await ReadAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            if (RobloxGlobalSettingsPolicy.Normalize(staged).Changed)
                throw new InvalidDataException("The staged Roblox settings did not retain the required values.");

            if (File.Exists(backupPath)) File.Delete(backupPath);
            RunSettingsMutation(
                _settingsPath,
                "clear-read-only",
                () => ClearReadOnlyAttribute(_settingsPath));
            RunSettingsMutation(
                _settingsPath,
                "replace",
                () => File.Replace(temporaryPath, _settingsPath, backupPath, ignoreMetadataErrors: false));

            XDocument persisted = await ReadAsync(_settingsPath, cancellationToken).ConfigureAwait(false);
            if (RobloxGlobalSettingsPolicy.Normalize(persisted).Changed)
                throw new InvalidDataException("Roblox settings could not be verified after replacement.");
            File.Delete(backupPath);
            return result;
        }
        catch
        {
            RestoreBackupIfPresent();
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private string BackupPath => $"{_settingsPath}.lilacmacro-backup";

    private static void ClearReadOnlyAttribute(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) == 0) return;
        File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }

    private static void RunSettingsMutation(string path, string operation, Action action)
    {
        try { action(); }
        catch (UnauthorizedAccessException error)
        {
            throw RobloxSettingsAccessException.Create(path, operation, error);
        }
    }

    private void RecoverInterruptedReplacement()
    {
        if (!File.Exists(BackupPath)) return;
        if (!File.Exists(_settingsPath))
        {
            File.Move(BackupPath, _settingsPath);
            return;
        }

        try
        {
            XDocument current = Read(_settingsPath);
            if (!RobloxGlobalSettingsPolicy.Normalize(current).Changed)
            {
                File.Delete(BackupPath);
                return;
            }
        }
        catch (Exception error) when (error is IOException or XmlException or InvalidDataException)
        {
        }
        RestoreBackupIfPresent();
    }

    private void RestoreBackupIfPresent()
    {
        if (!File.Exists(BackupPath)) return;
        if (File.Exists(_settingsPath)) File.Replace(BackupPath, _settingsPath, null, ignoreMetadataErrors: false);
        else File.Move(BackupPath, _settingsPath);
    }

    private static async Task<XDocument> ReadAsync(string path, CancellationToken cancellationToken)
    {
        ValidateFile(path);
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        using XmlReader reader = XmlReader.Create(stream, ReaderSettings(async: true));
        return await XDocument.LoadAsync(reader, LoadOptions.PreserveWhitespace, cancellationToken).ConfigureAwait(false);
    }

    private static XDocument Read(string path)
    {
        ValidateFile(path);
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using XmlReader reader = XmlReader.Create(stream, ReaderSettings(async: false));
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static async Task WriteAsync(string path, XDocument document, CancellationToken cancellationToken)
    {
        XmlWriterSettings settings = new()
        {
            Encoding = Utf8WithoutBom,
            Indent = false,
            OmitXmlDeclaration = document.Declaration is null,
            CloseOutput = false,
        };
        using MemoryStream buffer = new();
        using (XmlWriter writer = XmlWriter.Create(buffer, settings))
        {
            document.Save(writer);
        }
        if (buffer.Length > MaximumSettingsBytes)
            throw new InvalidDataException("Normalized Roblox settings exceed the size limit.");

        await using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await stream.WriteAsync(buffer.ToArray(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static XmlReaderSettings ReaderSettings(bool async) => new()
    {
        Async = async,
        DtdProcessing = DtdProcessing.Prohibit,
        IgnoreWhitespace = false,
        MaxCharactersInDocument = MaximumSettingsBytes,
        XmlResolver = null,
    };

    private static void ValidateFile(string path)
    {
        FileInfo file = new(path);
        if (!file.Exists)
            throw new RobloxSettingsMissingException(path);
        if (file.Length is <= 0 or > MaximumSettingsBytes)
            throw new InvalidDataException("Roblox settings have an invalid size.");
    }
}

public sealed class RobloxSettingsMissingException : FileNotFoundException
{
    internal RobloxSettingsMissingException(string path)
        : base(
            "Roblox settings are missing. Launch and close Roblox once, then start the macro again.",
            path)
    {
    }
}

public sealed class RobloxSettingsAccessException : IOException
{
    private RobloxSettingsAccessException(string message, Exception innerException)
        : base(message, innerException) { }

    internal static RobloxSettingsAccessException Create(
        string path,
        string operation,
        UnauthorizedAccessException error)
    {
        string attributes;
        try { attributes = File.GetAttributes(path).ToString(); }
        catch (Exception attributeError) when (attributeError is IOException or UnauthorizedAccessException)
        {
            attributes = "unavailable";
        }
        return new RobloxSettingsAccessException(
            $"LilacMacro cannot update Roblox settings (operation: {operation}, attributes: {attributes}, " +
            $"HRESULT: 0x{error.HResult:X8}). " +
            "Close Roblox and allow this Windows account to modify its Roblox settings file, then try again.",
            error);
    }
}
