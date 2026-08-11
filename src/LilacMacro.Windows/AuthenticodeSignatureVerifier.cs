using System.Runtime.InteropServices;

namespace LilacMacro.Windows;

public static class AuthenticodeSignatureVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static void VerifyTrusted(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The signed file is missing.", fullPath);

        WinTrustFileInfo file = new(fullPath);
        WinTrustData data = new(file.Pointer);
        try
        {
            uint result = WinVerifyTrust(nint.Zero, GenericVerifyV2, data.Pointer);
            if (result != 0)
                throw new InvalidDataException($"The file does not have a trusted Authenticode signature (0x{result:X8}).");
        }
        finally
        {
            data.Dispose();
            file.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeFileInfo
    {
        public uint StructSize;
        public nint FilePath;
        public nint FileHandle;
        public nint KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeTrustData
    {
        public uint StructSize;
        public nint PolicyCallbackData;
        public nint SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public nint FileInfo;
        public uint StateAction;
        public nint StateData;
        public nint UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
    }

    private sealed class WinTrustFileInfo : IDisposable
    {
        private readonly nint path;
        public nint Pointer { get; }

        public WinTrustFileInfo(string filePath)
        {
            path = Marshal.StringToCoTaskMemUni(filePath);
            NativeFileInfo value = new()
            {
                StructSize = (uint)Marshal.SizeOf<NativeFileInfo>(),
                FilePath = path,
            };
            Pointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeFileInfo>());
            Marshal.StructureToPtr(value, Pointer, false);
        }

        public void Dispose()
        {
            Marshal.FreeCoTaskMem(Pointer);
            Marshal.FreeCoTaskMem(path);
        }
    }

    private sealed class WinTrustData : IDisposable
    {
        public nint Pointer { get; }

        public WinTrustData(nint fileInfo)
        {
            NativeTrustData value = new()
            {
                StructSize = (uint)Marshal.SizeOf<NativeTrustData>(),
                UiChoice = 2,
                RevocationChecks = 1,
                UnionChoice = 1,
                FileInfo = fileInfo,
                ProviderFlags = 0x00000080,
            };
            Pointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeTrustData>());
            Marshal.StructureToPtr(value, Pointer, false);
        }

        public void Dispose() => Marshal.FreeCoTaskMem(Pointer);
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern uint WinVerifyTrust(
        nint window,
        [MarshalAs(UnmanagedType.LPStruct)] Guid action,
        nint trustData);
}
