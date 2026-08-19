using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace LilacMacro.Windows;

public sealed class InternetConnectivityProbe
{
    public bool IsAvailable()
    {
        try
        {
            if (!NetworkInterface.GetIsNetworkAvailable()) return false;
            return InternetGetConnectedState(out _, 0);
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    [DllImport("wininet.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetGetConnectedState(out int flags, int reserved);
}
