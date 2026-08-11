using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace LilacMacro.Windows.LocalSession;

public sealed class RunnerCredentialManager
{
    internal const int CredentialType = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@$%*-_";

    public static string CreateRandomPassword(int length = 48)
    {
        if (length < 32) throw new ArgumentOutOfRangeException(nameof(length));
        Span<char> password = length <= 128 ? stackalloc char[length] : new char[length];
        for (int index = 0; index < password.Length; index++)
            password[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(password);
    }

    public void Write(string targetName, string userName, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        byte[] secret = Encoding.Unicode.GetBytes(password);
        nint blob = Marshal.AllocCoTaskMem(secret.Length);
        try
        {
            Marshal.Copy(secret, 0, blob, secret.Length);
            Credential credential = new()
            {
                Type = CredentialType,
                TargetName = targetName,
                CredentialBlobSize = (uint)secret.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = userName,
            };
            if (!CredWrite(ref credential, 0))
                throw CredentialError(Marshal.GetLastWin32Error(), "Windows Credential Manager rejected the runner credential.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            ZeroAndFree(blob, secret.Length);
        }
    }

    public string ReadPassword(string targetName)
    {
        if (!CredRead(targetName, CredentialType, 0, out nint pointer))
            throw CredentialError(Marshal.GetLastWin32Error(), "Runner credential was not found.");
        try
        {
            Credential credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlob == nint.Zero || credential.CredentialBlobSize == 0)
                throw new InvalidDataException("Runner credential is empty.");
            return Marshal.PtrToStringUni(credential.CredentialBlob, checked((int)credential.CredentialBlobSize / 2))
                ?? throw new InvalidDataException("Runner credential could not be decoded.");
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Delete(string targetName)
    {
        if (CredDelete(targetName, CredentialType, 0)) return;
        int error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
            throw CredentialError(error, "Windows Credential Manager could not remove the runner credential.");
    }

    public bool Exists(string targetName)
    {
        if (!CredRead(targetName, CredentialType, 0, out nint pointer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return false;
            throw CredentialError(error, "Windows Credential Manager could not inspect the runner credential.");
        }
        CredFree(pointer);
        return true;
    }

    private static void ZeroAndFree(nint pointer, int length)
    {
        if (pointer == nint.Zero) return;
        for (int index = 0; index < length; index++) Marshal.WriteByte(pointer, index, 0);
        Marshal.FreeCoTaskMem(pointer);
    }

    internal static Win32Exception CredentialError(int code, string action) =>
        new(code, $"{action} {new Win32Exception(code).Message} (Win32 {code}).");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int flags, out nint credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(nint buffer);
}
