using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using LilacMacro.Core.Security;

namespace LilacMacro.Windows;

public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LilacMacro:macro-settings:v1");
    private const int UiForbidden = 1;
    private const int LocalMachine = 4;
    private readonly int _flags;

    public DpapiSecretProtector(bool machineScope = false) =>
        _flags = UiForbidden | (machineScope ? LocalMachine : 0);

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        return Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(plaintext), protect: true));
    }

    public string Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return string.Empty;
        try
        {
            return Encoding.UTF8.GetString(Transform(Convert.FromBase64String(protectedValue), protect: false));
        }
        catch (FormatException error)
        {
            throw new InvalidDataException("The stored secret is not valid.", error);
        }
    }

    private byte[] Transform(byte[] input, bool protect)
    {
        GCHandle inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        GCHandle entropyHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        DataBlob inputBlob = new() { Length = input.Length, Data = inputHandle.AddrOfPinnedObject() };
        DataBlob entropyBlob = new() { Length = Entropy.Length, Data = entropyHandle.AddrOfPinnedObject() };
        DataBlob outputBlob = default;
        try
        {
            bool success = protect
                ? CryptProtectData(ref inputBlob, "LilacMacro", ref entropyBlob, nint.Zero, nint.Zero, _flags, out outputBlob)
                : CryptUnprotectData(ref inputBlob, nint.Zero, ref entropyBlob, nint.Zero, nint.Zero, _flags, out outputBlob);
            if (!success)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not protect the saved secret.");
            byte[] output = new byte[outputBlob.Length];
            Marshal.Copy(outputBlob.Data, output, 0, output.Length);
            return output;
        }
        finally
        {
            if (outputBlob.Data != nint.Zero) LocalFree(outputBlob.Data);
            if (entropyHandle.IsAllocated) entropyHandle.Free();
            if (inputHandle.IsAllocated) inputHandle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public nint Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string description, ref DataBlob entropy, nint reserved, nint prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, nint description, ref DataBlob entropy, nint reserved, nint prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
