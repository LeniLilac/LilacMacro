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

                            float? measuredWhite = DisplayConfigQuery.TryGetSdrWhiteLevelNits(
                                description.DeviceName);
                            return FromOutputObservation(
                                description.ColorSpace,
                                measuredWhite,
                                description.MaxLuminance);
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

    internal static CaptureColorContext FromOutputObservation(
        ColorSpaceType colorSpace,
        float? measuredSdrWhiteNits,
        float displayMaxLuminanceNits)
    {
        bool colorSpaceAdvanced = colorSpace != ColorSpaceType.RgbFullG22NoneP709;
        bool whiteLevelAdvanced = measuredSdrWhiteNits is >
            CaptureColorContext.SceneReferenceWhiteNits + 1f;
        bool advanced = colorSpaceAdvanced || whiteLevelAdvanced;
        bool fallback = advanced && measuredSdrWhiteNits is not > 0f;
        float white = advanced
            ? measuredSdrWhiteNits ?? CaptureColorContext.AdvancedColorFallbackWhiteNits
            : CaptureColorContext.SceneReferenceWhiteNits;
        string detection = colorSpaceAdvanced
            ? measuredSdrWhiteNits is > 0f ? "advanced-color-space+measured-white" : "advanced-color-space+fallback-white"
            : whiteLevelAdvanced ? "elevated-sdr-white" : "sdr-color-space";
        return new CaptureColorContext(
            advanced,
            white,
            displayMaxLuminanceNits,
            colorSpace.ToString(),
            detection,
            fallback);
    }
}
