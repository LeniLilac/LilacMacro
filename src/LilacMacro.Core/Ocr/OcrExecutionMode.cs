namespace LilacMacro.Core.Ocr;

public enum OcrExecutionMode
{
    Automatic,
    GpuPreferred,
    CpuOnly,
}

public static class OcrExecutionModePolicy
{
    public static bool PrefersGpu(OcrExecutionMode mode) => mode is not OcrExecutionMode.CpuOnly;
}
