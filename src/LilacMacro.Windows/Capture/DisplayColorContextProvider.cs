using LilacMacro.Windows.Interop;
using Vortice.DXGI;

namespace LilacMacro.Windows.Capture;

internal static class DisplayColorContextProvider
{
    public static CaptureColorContext GetForWindow(nint window)
    {
        nint monitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
        if (monitor == nint.Zero) return CaptureColorContext.StandardSdr;

        try
        {
            using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint adapterIndex = 0; ; adapterIndex++)
            {
                if (factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1? adapter).Failure) break;
                using (adapter)
                {
                    for (uint outputIndex = 0; ; outputIndex++)
                    {
                        if (adapter.EnumOutputs(outputIndex, out IDXGIOutput? output).Failure) break;
                        using (output)
                        {
                            using IDXGIOutput6 output6 = output.QueryInterface<IDXGIOutput6>();
                            OutputDescription1 description = output6.Description1;
                            if (description.Monitor != monitor) continue;

                            bool advanced = description.ColorSpace != ColorSpaceType.RgbFullG22NoneP709;
                            float white = advanced
                                ? DisplayConfigQuery.TryGetSdrWhiteLevelNits(description.DeviceName) ?? 80f
                                : 80f;
                            return new CaptureColorContext(advanced, white, description.MaxLuminance);
                        }
                    }
                }
            }
        }
        catch
        {
            // Capture must remain available when a driver does not expose IDXGIOutput6.
        }
        return CaptureColorContext.StandardSdr;
    }
}
