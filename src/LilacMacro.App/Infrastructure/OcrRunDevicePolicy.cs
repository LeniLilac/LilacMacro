namespace LilacMacro.App.Infrastructure;

internal enum OcrGpuFailureDecision
{
    Rethrow,
    RetryGpu,
    UseCpu,
}

internal sealed class OcrRunDevicePolicy
{
    private readonly object _gate = new();
    private bool _active;
    private bool _cpuFallback;
    private int _consecutiveGpuFailures;

    public void Begin()
    {
        lock (_gate)
        {
            _active = true;
            _cpuFallback = false;
            _consecutiveGpuFailures = 0;
        }
    }

    public void End()
    {
        lock (_gate)
        {
            _active = false;
            _cpuFallback = false;
            _consecutiveGpuFailures = 0;
        }
    }

    public string Resolve(string requestedDevice)
    {
        lock (_gate)
        {
            return _active && _cpuFallback && requestedDevice == OcrRunner.GpuDevice
                ? OcrRunner.CpuDevice
                : requestedDevice;
        }
    }

    public OcrGpuFailureDecision ObserveGpuFailure(bool cpuReady)
    {
        lock (_gate)
        {
            if (!_active || _cpuFallback) return OcrGpuFailureDecision.Rethrow;
            _consecutiveGpuFailures++;
            if (_consecutiveGpuFailures == 1) return OcrGpuFailureDecision.RetryGpu;
            if (!cpuReady) return OcrGpuFailureDecision.Rethrow;
            _cpuFallback = true;
            return OcrGpuFailureDecision.UseCpu;
        }
    }

    public OcrGpuFailureDecision ObserveStableGpuFailure(bool cpuReady)
    {
        lock (_gate)
        {
            if (!_active || _cpuFallback || !cpuReady) return OcrGpuFailureDecision.Rethrow;
            _cpuFallback = true;
            return OcrGpuFailureDecision.UseCpu;
        }
    }

    public void ObserveGpuSuccess()
    {
        lock (_gate)
        {
            if (_active && !_cpuFallback) _consecutiveGpuFailures = 0;
        }
    }
}
