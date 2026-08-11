using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LilacMacro.Core.LocalSession;

namespace LilacMacro.Windows.LocalSession;

internal sealed record RemoteDesktopCertificateObservation(
    string Thumbprint,
    bool PrivateKeyAccessible,
    string EncodedCertificate);

public sealed class RemoteDesktopCertificateManager
{
    internal const string BaselineKind = "rdp-certificate-baseline";
    internal const string CertificateKind = "rdp-certificate";
    internal const string UsableCertificateType = "x509-der-private-key-usable";
    internal const string MissingKeyCertificateType = "x509-der-private-key-missing";
    private const string StoreName = "Remote Desktop";

    public IReadOnlyList<OriginalSystemValue> CaptureBaseline()
    {
        List<OriginalSystemValue> baseline =
        [
            new(BaselineKind, StoreName, false, null, null),
        ];
        foreach (RemoteDesktopCertificateObservation certificate in Observe())
        {
            baseline.Add(new OriginalSystemValue(
                CertificateKind,
                certificate.Thumbprint,
                true,
                certificate.PrivateKeyAccessible ? UsableCertificateType : MissingKeyCertificateType,
                certificate.EncodedCertificate));
        }
        return baseline;
    }

    public void RemoveCertificatesWithMissingKeys(IEnumerable<OriginalSystemValue> originalSystemState)
    {
        HashSet<string> broken = BrokenBaselineThumbprints(originalSystemState);
        if (broken.Count == 0) return;

        using X509Store store = OpenStore(OpenFlags.ReadWrite);
        foreach (X509Certificate2 certificate in store.Certificates)
        {
            using (certificate)
            {
                if (broken.Contains(NormalizeThumbprint(certificate.Thumbprint))) store.Remove(certificate);
            }
        }
    }

    public void RestoreBaseline(IEnumerable<OriginalSystemValue> originalSystemState)
    {
        OriginalSystemValue[] originals = [.. originalSystemState];
        if (!HasBaseline(originals)) return;
        Dictionary<string, OriginalSystemValue> baseline = BaselineCertificates(originals);

        using X509Store store = OpenStore(OpenFlags.ReadWrite);
        foreach (X509Certificate2 certificate in store.Certificates)
        {
            using (certificate)
            {
                if (!baseline.ContainsKey(NormalizeThumbprint(certificate.Thumbprint))) store.Remove(certificate);
            }
        }

        HashSet<string> current = Observe(store).Select(item => item.Thumbprint).ToHashSet(StringComparer.Ordinal);
        foreach ((string thumbprint, OriginalSystemValue original) in baseline)
        {
            if (current.Contains(thumbprint)) continue;
            if (string.Equals(original.ValueType, UsableCertificateType, StringComparison.Ordinal))
                throw new InvalidOperationException($"A pre-existing RDP certificate with a usable private key disappeared: {thumbprint}.");
            byte[] encoded = Convert.FromBase64String(original.EncodedValue!);
            using X509Certificate2 restored = X509CertificateLoader.LoadCertificate(encoded);
            if (!string.Equals(NormalizeThumbprint(restored.Thumbprint), thumbprint, StringComparison.Ordinal))
                throw new InvalidDataException("The journaled RDP certificate thumbprint does not match its encoded certificate.");
            store.Add(restored);
        }
    }

    public IReadOnlyList<string> FindRestoreMismatches(IEnumerable<OriginalSystemValue> originalSystemState)
    {
        OriginalSystemValue[] originals = [.. originalSystemState];
        if (!HasBaseline(originals)) return [];
        return CompareBaseline(originals, Observe());
    }

    internal static IReadOnlyList<string> CompareBaseline(
        IEnumerable<OriginalSystemValue> originalSystemState,
        IEnumerable<RemoteDesktopCertificateObservation> currentCertificates)
    {
        Dictionary<string, OriginalSystemValue> baseline = BaselineCertificates(originalSystemState);
        Dictionary<string, RemoteDesktopCertificateObservation> current = currentCertificates
            .ToDictionary(item => NormalizeThumbprint(item.Thumbprint), StringComparer.Ordinal);
        List<string> problems = [];
        foreach (string unexpected in current.Keys.Except(baseline.Keys, StringComparer.Ordinal))
            problems.Add($"Generated RDP certificate remains: {unexpected}");
        foreach ((string thumbprint, OriginalSystemValue original) in baseline)
        {
            if (!current.TryGetValue(thumbprint, out RemoteDesktopCertificateObservation? observed))
            {
                problems.Add($"Original RDP certificate was not restored: {thumbprint}");
                continue;
            }
            bool expectedUsable = string.Equals(original.ValueType, UsableCertificateType, StringComparison.Ordinal);
            if (observed.PrivateKeyAccessible != expectedUsable)
                problems.Add($"Original RDP certificate private-key state differs: {thumbprint}");
            if (!string.Equals(observed.EncodedCertificate, original.EncodedValue, StringComparison.Ordinal))
                problems.Add($"Original RDP certificate data differs: {thumbprint}");
        }
        return problems;
    }

    internal static HashSet<string> BrokenBaselineThumbprints(IEnumerable<OriginalSystemValue> originals) =>
        originals
            .Where(item => string.Equals(item.Kind, CertificateKind, StringComparison.Ordinal)
                && string.Equals(item.ValueType, MissingKeyCertificateType, StringComparison.Ordinal))
            .Select(item => NormalizeThumbprint(item.Identifier))
            .ToHashSet(StringComparer.Ordinal);

    private static bool HasBaseline(IEnumerable<OriginalSystemValue> originals) =>
        originals.Any(item => string.Equals(item.Kind, BaselineKind, StringComparison.Ordinal)
            && string.Equals(item.Identifier, StoreName, StringComparison.Ordinal));

    private static Dictionary<string, OriginalSystemValue> BaselineCertificates(IEnumerable<OriginalSystemValue> originals) =>
        originals
            .Where(item => string.Equals(item.Kind, CertificateKind, StringComparison.Ordinal))
            .ToDictionary(item => NormalizeThumbprint(item.Identifier), StringComparer.Ordinal);

    private static IReadOnlyList<RemoteDesktopCertificateObservation> Observe()
    {
        using X509Store store = OpenStore(OpenFlags.ReadOnly);
        return Observe(store);
    }

    private static IReadOnlyList<RemoteDesktopCertificateObservation> Observe(X509Store store)
    {
        List<RemoteDesktopCertificateObservation> observed = [];
        foreach (X509Certificate2 certificate in store.Certificates)
        {
            using (certificate)
            {
                observed.Add(new RemoteDesktopCertificateObservation(
                    NormalizeThumbprint(certificate.Thumbprint),
                    CanOpenPrivateKey(certificate),
                    Convert.ToBase64String(certificate.Export(X509ContentType.Cert))));
            }
        }
        return observed;
    }

    private static X509Store OpenStore(OpenFlags flags)
    {
        X509Store store = new(StoreName, StoreLocation.LocalMachine);
        store.Open(flags);
        return store;
    }

    private static bool CanOpenPrivateKey(X509Certificate2 certificate)
    {
        try
        {
            using AsymmetricAlgorithm? key = certificate.GetRSAPrivateKey() ?? (AsymmetricAlgorithm?)certificate.GetECDsaPrivateKey();
            return key is not null;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string NormalizeThumbprint(string value) =>
        new(value.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
}
