using System.Text.Json;
using System.Text.Json.Serialization;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace LilacMacro.Core.Updates;

public sealed record ReleaseInstallerManifest
{
    public string Name { get; init; } = string.Empty;
    public long Size { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}

public sealed record ReleaseManifest
{
    public string Format { get; init; } = string.Empty;
    public int SchemaVersion { get; init; }
    public string KeyId { get; init; } = string.Empty;
    public string Algorithm { get; init; } = string.Empty;
    public string Tag { get; init; } = string.Empty;
    public string SourceCommit { get; init; } = string.Empty;
    public ReleaseInstallerManifest? Installer { get; init; }
}

public sealed class ReleaseManifestVerifier
{
    public const int MaximumManifestBytes = 4096;
    private const int SignatureSize = 64;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly ReleaseTrust _trust;

    public ReleaseManifestVerifier() : this(ReleaseTrust.LoadEmbedded())
    {
    }

    public ReleaseManifestVerifier(string keyId, string publicKeySpkiBase64) :
        this(new ReleaseTrust(keyId, ParsePublicKey(publicKeySpkiBase64)))
    {
    }

    private ReleaseManifestVerifier(ReleaseTrust trust) => _trust = trust;

    public string Verify(
        ReadOnlyMemory<byte> manifestBytes,
        string encodedSignature,
        VerifiedUpdateRelease release)
    {
        ArgumentNullException.ThrowIfNull(encodedSignature);
        ArgumentNullException.ThrowIfNull(release);
        if (manifestBytes.Length is < 2 or > MaximumManifestBytes)
            throw new InvalidDataException("The signed release manifest size was invalid.");
        byte[] signature;
        try { signature = Convert.FromBase64String(encodedSignature.Trim()); }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The release signature encoding was invalid.", exception);
        }
        if (signature.Length != SignatureSize)
            throw new InvalidDataException("The release signature size was invalid.");

        Ed25519Signer verifier = new();
        verifier.Init(false, _trust.PublicKey);
        byte[] bytes = manifestBytes.ToArray();
        verifier.BlockUpdate(bytes, 0, bytes.Length);
        if (!verifier.VerifySignature(signature))
            throw new InvalidDataException("The release manifest signature was invalid.");

        ReleaseManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ReleaseManifest>(manifestBytes.Span, JsonOptions)
                ?? throw new InvalidDataException("The release manifest was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The release manifest was invalid.", exception);
        }
        Validate(manifest, release);
        return manifest.Installer!.Sha256.ToUpperInvariant();
    }

    private void Validate(ReleaseManifest manifest, VerifiedUpdateRelease release)
    {
        if (manifest.Format != "lilacmacro.release" || manifest.SchemaVersion != 1 ||
            manifest.KeyId != _trust.KeyId || manifest.Algorithm != "Ed25519" ||
            manifest.Tag != release.TagName || !IsCommit(manifest.SourceCommit) ||
            manifest.Installer is null ||
            manifest.Installer.Name != GitHubReleasePolicy.InstallerName ||
            manifest.Installer.Size != release.Installer.Size ||
            !IsSha256(manifest.Installer.Sha256))
        {
            throw new InvalidDataException("The signed release manifest did not match the selected GitHub release.");
        }
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsCommit(string value) => value.Length == 40 && value.All(character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static Ed25519PublicKeyParameters ParsePublicKey(string encoded)
    {
        try
        {
            byte[] der = Convert.FromBase64String(encoded);
            return PublicKeyFactory.CreateKey(der) as Ed25519PublicKeyParameters
                ?? throw new InvalidDataException("The release signing key was not Ed25519.");
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new InvalidDataException("The release signing key was invalid.", exception);
        }
    }

    private sealed record ReleaseTrust(string KeyId, Ed25519PublicKeyParameters PublicKey)
    {
        public static ReleaseTrust LoadEmbedded()
        {
            using Stream stream = typeof(ReleaseManifestVerifier).Assembly.GetManifestResourceStream("LilacMacro.ReleaseTrust.json")
                ?? throw new InvalidOperationException("The embedded release trust policy is missing.");
            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            if (root.GetProperty("format").GetString() != "lilacmacro.release-trust" ||
                root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("algorithm").GetString() != "Ed25519")
                throw new InvalidDataException("The embedded release trust policy was invalid.");
            string keyId = root.GetProperty("keyId").GetString() ?? string.Empty;
            string publicKey = root.GetProperty("publicKeySpkiBase64").GetString() ?? string.Empty;
            if (keyId.Length is < 1 or > 32) throw new InvalidDataException("The release trust key ID was invalid.");
            return new ReleaseTrust(keyId, ParsePublicKey(publicKey));
        }
    }
}
