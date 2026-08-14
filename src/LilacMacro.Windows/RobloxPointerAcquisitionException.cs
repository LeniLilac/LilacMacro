namespace LilacMacro.Windows;

internal sealed class RobloxPointerAcquisitionException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);
