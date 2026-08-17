namespace LilacMacro.App.Infrastructure;

public sealed record OcrGpuInfo(
    string Name,
    string Generation,
    double ComputeCapability,
    string DriverVersion,
    string CudaFeed);
