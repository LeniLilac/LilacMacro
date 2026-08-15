using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace LilacMacro.Core.Services;

public sealed class ControlSnapshotVerifier
{
    private const int Ed25519SignatureSize = 64;

    public const int MaximumSnapshotBytes = 256 * 1024;
    public static readonly TimeSpan MaximumSnapshotLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMinutes(1);

    private readonly IReadOnlyDictionary<string, Ed25519PublicKeyParameters> _trustedKeys;

    public ControlSnapshotVerifier(IReadOnlyDictionary<string, string> trustedSpkiKeys)
    {
        ArgumentNullException.ThrowIfNull(trustedSpkiKeys);
        if (trustedSpkiKeys.Count is < 1 or > 4)
            throw new ArgumentException("One to four trusted control keys are required.", nameof(trustedSpkiKeys));

        Dictionary<string, Ed25519PublicKeyParameters> keys = new(StringComparer.Ordinal);
        foreach ((string keyId, string encodedKey) in trustedSpkiKeys)
        {
            if (keyId.Length is < 1 or > 32 || !keys.TryAdd(keyId, ParseKey(encodedKey)))
                throw new ArgumentException("A trusted control key was invalid.", nameof(trustedSpkiKeys));
        }
        _trustedKeys = keys;
    }

    public SignedControlSnapshot Verify(
        ReadOnlyMemory<byte> utf8Json,
        DateTimeOffset now,
        long minimumRevision)
    {
        SignedControlSnapshot snapshot = VerifySignature(utf8Json);
        ValidateFreshness(snapshot.Payload, now, minimumRevision);
        return snapshot;
    }

    public SignedControlSnapshot VerifySignature(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.Length is < 2 or > MaximumSnapshotBytes)
            throw new InvalidDataException("Control snapshot size was invalid.");

        using JsonDocument document = JsonDocument.Parse(utf8Json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 16,
        });
        SignedControlSnapshot snapshot = ControlSnapshotJsonReader.Read(document.RootElement);
        if (!_trustedKeys.TryGetValue(snapshot.KeyId, out Ed25519PublicKeyParameters? key))
            throw new InvalidDataException("Control snapshot key ID was not trusted.");

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(snapshot.Signature);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("Control snapshot signature encoding was invalid.", exception);
        }
        if (signature.Length != Ed25519SignatureSize)
            throw new InvalidDataException("Control snapshot signature size was invalid.");

        byte[] canonicalPayload = ControlCanonicalJson.Encode(document.RootElement.GetProperty("payload"));
        Ed25519Signer signer = new();
        signer.Init(false, key);
        signer.BlockUpdate(canonicalPayload, 0, canonicalPayload.Length);
        if (!signer.VerifySignature(signature))
            throw new InvalidDataException("Control snapshot signature was invalid.");

        return snapshot;
    }

    public static void ValidateFreshness(
        ControlPayload payload,
        DateTimeOffset now,
        long minimumRevision)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (minimumRevision < 0) throw new ArgumentOutOfRangeException(nameof(minimumRevision));
        if (payload.Revision < minimumRevision)
            throw new InvalidDataException("Control snapshot revision rolled back.");
        if (payload.GeneratedAt > now + MaximumFutureSkew)
            throw new InvalidDataException("Control snapshot was generated in the future.");
        if (payload.ExpiresAt <= payload.GeneratedAt)
            throw new InvalidDataException("Control snapshot expiry did not follow generation.");
        if (payload.ExpiresAt - payload.GeneratedAt > MaximumSnapshotLifetime)
            throw new InvalidDataException("Control snapshot lifetime exceeded the allowed bound.");
        if (payload.ExpiresAt <= now)
            throw new InvalidDataException("Control snapshot expired.");
        if (payload.Codes.Any(code => code.ExpiresAt is not null && code.ExpiresAt <= payload.GeneratedAt))
            throw new InvalidDataException("Control snapshot contained an expired redeem code.");
        if (payload.Disablements.Any(item =>
                item.ExpiresAt is not null && item.ExpiresAt <= payload.GeneratedAt))
            throw new InvalidDataException("Control snapshot contained an expired disablement.");
    }

    private static Ed25519PublicKeyParameters ParseKey(string encodedKey)
    {
        try
        {
            byte[] der = Convert.FromBase64String(encodedKey);
            if (der.Length is < 32 or > 128)
                throw new InvalidDataException("Trusted control key size was invalid.");
            return PublicKeyFactory.CreateKey(der) as Ed25519PublicKeyParameters
                ?? throw new InvalidDataException("Trusted control key was not Ed25519.");
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new InvalidDataException("Trusted control key encoding was invalid.", exception);
        }
    }
}
