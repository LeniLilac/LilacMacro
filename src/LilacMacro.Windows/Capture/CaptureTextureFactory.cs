using Vortice.Direct3D11;
using Vortice.DXGI;

namespace LilacMacro.Windows.Capture;

internal static class CaptureTextureFactory
{
    public static ID3D11Texture2D Create(ID3D11Device device, int width, int height)
    {
        Texture2DDescription description = new(
            Format.R16G16B16A16_Float,
            checked((uint)width),
            checked((uint)height),
            1,
            1,
            BindFlags.None,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            1,
            0,
            ResourceOptionFlags.None);
        return device.CreateTexture2D(description);
    }
}
