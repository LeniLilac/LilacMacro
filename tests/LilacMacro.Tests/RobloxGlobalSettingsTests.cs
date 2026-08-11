using System.Xml.Linq;
using LilacMacro.Core.Roblox;
using LilacMacro.Windows;

namespace LilacMacro.Tests;

public sealed class RobloxGlobalSettingsTests
{
    [Fact]
    public void Normalization_changes_only_the_allowlisted_ui_and_input_settings()
    {
        XDocument document = XDocument.Parse(SettingsXml());
        string referent = document.Descendants("Item").Single().Attribute("referent")!.Value;

        RobloxSettingsNormalizationResult result = RobloxGlobalSettingsPolicy.Normalize(document);

        Assert.True(result.Changed);
        Assert.Equal(23, result.ChangedSettings.Count);
        Assert.Contains("PreferredTextSize", result.ChangedSettings);
        Assert.Contains("MouseSensitivityThirdPerson.Y", result.ChangedSettings);
        Assert.Equal("1", Value(document, "PreferredTextSize"));
        Assert.Equal("false", Value(document, "ChatVisible"));
        Assert.Equal("false", Value(document, "PerformanceStatsVisible"));
        Assert.Equal("true", Value(document, "PlayerListVisible"));
        Assert.Equal("1", Value(document, "MouseSensitivity"));
        Assert.Equal("1", Value(document, "ComputerCameraMovementMode"));
        Assert.Equal("1", Value(document, "ComputerMovementMode"));
        Assert.Equal("1", Value(document, "ControlMode"));
        Assert.Equal("false", Value(document, "Fullscreen"));
        Assert.Equal("false", Value(document, "UiNavigationKeyBindEnabled"));
        Assert.Equal(referent, document.Descendants("Item").Single().Attribute("referent")!.Value);
        Assert.Equal("preserved", Value(document, "UnrelatedSetting"));
        Assert.False(RobloxGlobalSettingsPolicy.Normalize(document).Changed);
    }

    [Theory]
    [InlineData("<unexpected />")]
    [InlineData("<roblox />")]
    [InlineData("<roblox><Item class=\"UserGameSettings\" /><Item class=\"UserGameSettings\" /></roblox>")]
    public void Malformed_or_ambiguous_documents_fail_closed(string xml)
    {
        Assert.Throws<InvalidDataException>(() => RobloxGlobalSettingsPolicy.Normalize(XDocument.Parse(xml)));
    }

    [Fact]
    public void Missing_duplicated_or_wrongly_typed_required_settings_fail_closed()
    {
        XDocument missing = XDocument.Parse(SettingsXml());
        missing.Descendants().Single(element => (string?)element.Attribute("name") == "ChatVisible").Remove();
        Assert.Throws<InvalidDataException>(() => RobloxGlobalSettingsPolicy.Normalize(missing));

        XDocument duplicated = XDocument.Parse(SettingsXml());
        duplicated.Descendants("Properties").Single().Add(new XElement("bool", new XAttribute("name", "ChatVisible"), "true"));
        Assert.Throws<InvalidDataException>(() => RobloxGlobalSettingsPolicy.Normalize(duplicated));

        XDocument wrongType = XDocument.Parse(SettingsXml());
        wrongType.Descendants().Single(element => (string?)element.Attribute("name") == "MouseSensitivity").Name = "token";
        Assert.Throws<InvalidDataException>(() => RobloxGlobalSettingsPolicy.Normalize(wrongType));
    }

    [Fact]
    public async Task Store_atomically_persists_and_revalidates_the_document()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-roblox-settings-{Guid.NewGuid():N}");
        string path = Path.Combine(root, "GlobalBasicSettings_13.xml");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(path, SettingsXml());
            RobloxSettingsNormalizationResult result = await new RobloxGlobalSettingsStore(path).NormalizeAsync();

            Assert.True(result.Changed);
            XDocument persisted = XDocument.Load(path);
            Assert.False(RobloxGlobalSettingsPolicy.Normalize(persisted).Changed);
            Assert.False(File.Exists($"{path}.lilacmacro-backup"));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Store_recovers_an_interrupted_replacement_before_normalizing()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-roblox-recovery-{Guid.NewGuid():N}");
        string path = Path.Combine(root, "GlobalBasicSettings_13.xml");
        Directory.CreateDirectory(root);
        try
        {
            XDocument backup = XDocument.Parse(SettingsXml());
            RobloxGlobalSettingsPolicy.Normalize(backup);
            backup.Save($"{path}.lilacmacro-backup");
            await File.WriteAllTextAsync(path, "<corrupt />");

            RobloxSettingsNormalizationResult result = await new RobloxGlobalSettingsStore(path).NormalizeAsync();

            Assert.False(result.Changed);
            Assert.False(File.Exists($"{path}.lilacmacro-backup"));
            Assert.False(RobloxGlobalSettingsPolicy.Normalize(XDocument.Load(path)).Changed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Store_rejects_missing_and_oversized_documents()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-roblox-bounds-{Guid.NewGuid():N}");
        string path = Path.Combine(root, "GlobalBasicSettings_13.xml");
        Directory.CreateDirectory(root);
        try
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() => new RobloxGlobalSettingsStore(path).NormalizeAsync());
            await File.WriteAllTextAsync(path, new string('x', (1024 * 1024) + 1));
            await Assert.ThrowsAsync<InvalidDataException>(() => new RobloxGlobalSettingsStore(path).NormalizeAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Process_filter_is_exact_and_case_insensitive()
    {
        Assert.True(RobloxClientLifecycleService.IsSupportedClient("RobloxPlayerBeta"));
        Assert.True(RobloxClientLifecycleService.IsSupportedClient("windows10universal"));
        Assert.False(RobloxClientLifecycleService.IsSupportedClient("RobloxCrashHandler"));
        Assert.False(RobloxClientLifecycleService.IsSupportedClient("RobloxStudioBeta"));
    }

    [Fact]
    public void Lifecycle_repeats_graceful_close_before_tree_termination()
    {
        Assert.Equal(2, RobloxClientLifecycleService.GracefulCloseAttemptCount);
        Assert.Equal(2, RobloxClientLifecycleService.ForcedCloseAttemptCount);
        Assert.Equal(TimeSpan.FromSeconds(4), RobloxClientLifecycleService.GracefulCloseAttemptTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), RobloxClientLifecycleService.ForcedRespawnSettleTime);
    }

    private static string Value(XDocument document, string name) => document.Descendants()
        .Single(element => (string?)element.Attribute("name") == name)
        .Value;

    private static string SettingsXml() => """
        <roblox version="4">
          <Item class="UserGameSettings" referent="RBX-TEST-REFERENT">
            <Properties>
              <token name="CameraMode">2</token>
              <bool name="CameraYInverted">true</bool>
              <bool name="ChatVisible">true</bool>
              <bool name="ComputerCameraMovementChanged">false</bool>
              <token name="ComputerCameraMovementMode">4</token>
              <bool name="ComputerMovementChanged">false</bool>
              <token name="ComputerMovementMode">2</token>
              <token name="ControlMode">0</token>
              <bool name="Fullscreen">true</bool>
              <bool name="MicroProfilerWebServerEnabled">true</bool>
              <float name="MouseSensitivity">0.2</float>
              <Vector2 name="MouseSensitivityFirstPerson"><X>0.2</X><Y>0.3</Y></Vector2>
              <Vector2 name="MouseSensitivityThirdPerson"><X>0.4</X><Y>0.5</Y></Vector2>
              <bool name="OnScreenProfilerEnabled">true</bool>
              <bool name="PerformanceStatsVisible">true</bool>
              <bool name="PlayerListVisible">false</bool>
              <token name="PreferredTextSize">4</token>
              <float name="PreferredTransparency">0.5</float>
              <bool name="ReducedMotion">true</bool>
              <bool name="UiNavigationKeyBindEnabled">true</bool>
              <bool name="VREnabled">true</bool>
              <string name="UnrelatedSetting">preserved</string>
            </Properties>
          </Item>
        </roblox>
        """;
}
