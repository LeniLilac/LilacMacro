namespace LilacMacro.App.Views;

internal sealed class PlacementWheelGesturePolicy(TimeSpan? burstWindow = null)
{
    private readonly TimeSpan _burstWindow = burstWindow ?? TimeSpan.FromMilliseconds(320);
    private DateTimeOffset? _lastWheelAt;
    private bool _mapOwnsBurst;

    public bool Observe(DateTimeOffset observedAt, bool pointerOverMap)
    {
        if (_lastWheelAt is null || observedAt - _lastWheelAt > _burstWindow)
        {
            _mapOwnsBurst = pointerOverMap;
        }

        _lastWheelAt = observedAt;
        return _mapOwnsBurst && pointerOverMap;
    }
}
