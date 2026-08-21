using LilacMacro.Core.Services;

namespace LilacMacro.App.Runtime;

internal static class PrivacyChoicesPolicy
{
    internal const int CurrentNoticeVersion = ProductTelemetryPolicy.CurrentPrivacyNoticeVersion;

    internal static readonly Uri PrivacyUri = new("https://macro.expeditions.gg/privacy");

    internal static readonly Uri TermsUri = new("https://macro.expeditions.gg/terms");
}
