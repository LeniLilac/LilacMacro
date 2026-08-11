using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using LilacMacro.Core.LocalSession;

namespace LilacMacro.Windows.LocalSession;

public static class SessionPipe
{
    public const string Name = "LilacMacro.LocalSession.v1";

    public static NamedPipeServerStream CreateServer(string ownerSid, string runnerSid)
    {
        PipeSecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (string sid in new[] { ownerSid, runnerSid, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value }.Distinct(StringComparer.Ordinal))
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid), PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            Name,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            16 * 1024,
            16 * 1024,
            security);
    }

    public static async Task WriteAsync<T>(PipeStream pipe, T message, CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, AtomicJsonFile.Options);
        if (payload.Length > 1024 * 1024) throw new InvalidDataException("Session pipe message exceeds the one-megabyte limit.");
        byte[] length = BitConverter.GetBytes(payload.Length);
        await pipe.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await pipe.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(PipeStream pipe, CancellationToken cancellationToken)
    {
        byte[] length = new byte[sizeof(int)];
        await pipe.ReadExactlyAsync(length, cancellationToken).ConfigureAwait(false);
        int count = BitConverter.ToInt32(length);
        if (count is <= 0 or > 1024 * 1024) throw new InvalidDataException("Session pipe message length is invalid.");
        byte[] payload = new byte[count];
        await pipe.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(payload, AtomicJsonFile.Options)
            ?? throw new InvalidDataException("Session pipe message could not be decoded.");
    }
}
