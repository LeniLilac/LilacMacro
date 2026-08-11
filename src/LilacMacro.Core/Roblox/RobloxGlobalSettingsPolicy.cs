using System.Xml.Linq;

namespace LilacMacro.Core.Roblox;

public sealed record RobloxSettingsNormalizationResult(IReadOnlyList<string> ChangedSettings)
{
    public bool Changed => ChangedSettings.Count > 0;
}

public static class RobloxGlobalSettingsPolicy
{
    private static readonly Setting[] RequiredSettings =
    [
        new("CameraMode", "token", "0"),
        new("CameraYInverted", "bool", "false"),
        new("ChatVisible", "bool", "false"),
        new("ComputerCameraMovementChanged", "bool", "true"),
        new("ComputerCameraMovementMode", "token", "1"),
        new("ComputerMovementChanged", "bool", "true"),
        new("ComputerMovementMode", "token", "1"),
        new("ControlMode", "token", "1"),
        new("Fullscreen", "bool", "false"),
        new("MicroProfilerWebServerEnabled", "bool", "false"),
        new("MouseSensitivity", "float", "1"),
        new("OnScreenProfilerEnabled", "bool", "false"),
        new("PerformanceStatsVisible", "bool", "false"),
        new("PlayerListVisible", "bool", "true"),
        new("PreferredTextSize", "token", "1"),
        new("PreferredTransparency", "float", "1"),
        new("ReducedMotion", "bool", "false"),
        new("UiNavigationKeyBindEnabled", "bool", "false"),
        new("VREnabled", "bool", "false"),
    ];

    private static readonly string[] SensitivityVectors =
    [
        "MouseSensitivityFirstPerson",
        "MouseSensitivityThirdPerson",
    ];

    public static RobloxSettingsNormalizationResult Normalize(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        XElement root = document.Root ?? throw new InvalidDataException("Roblox settings have no root element.");
        if (root.Name.LocalName != "roblox")
            throw new InvalidDataException("Roblox settings have an unexpected root element.");

        XElement[] settingsItems = root.Elements()
            .Where(element => element.Name.LocalName == "Item" &&
                string.Equals((string?)element.Attribute("class"), "UserGameSettings", StringComparison.Ordinal))
            .ToArray();
        if (settingsItems.Length != 1)
            throw new InvalidDataException("Roblox settings must contain exactly one UserGameSettings item.");

        XElement[] propertiesNodes = settingsItems[0].Elements()
            .Where(element => element.Name.LocalName == "Properties")
            .ToArray();
        if (propertiesNodes.Length != 1)
            throw new InvalidDataException("Roblox UserGameSettings must contain exactly one Properties element.");

        XElement properties = propertiesNodes[0];
        List<string> changed = [];
        foreach (Setting setting in RequiredSettings)
        {
            XElement node = RequiredProperty(properties, setting.Name, setting.ElementName);
            if (string.Equals(node.Value, setting.Value, StringComparison.Ordinal)) continue;
            node.Value = setting.Value;
            changed.Add(setting.Name);
        }

        foreach (string name in SensitivityVectors)
        {
            XElement vector = RequiredProperty(properties, name, "Vector2");
            foreach (string axis in new[] { "X", "Y" })
            {
                XElement[] nodes = vector.Elements().Where(element => element.Name.LocalName == axis).ToArray();
                if (nodes.Length != 1)
                    throw new InvalidDataException($"Roblox setting {name} must contain exactly one {axis} value.");
                if (string.Equals(nodes[0].Value, "1", StringComparison.Ordinal)) continue;
                nodes[0].Value = "1";
                changed.Add($"{name}.{axis}");
            }
        }

        return new RobloxSettingsNormalizationResult(changed);
    }

    private static XElement RequiredProperty(XElement properties, string name, string elementName)
    {
        XElement[] matches = properties.Elements()
            .Where(element => string.Equals((string?)element.Attribute("name"), name, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1 || matches[0].Name.LocalName != elementName)
            throw new InvalidDataException($"Roblox setting {name} is missing, duplicated, or has an unexpected type.");
        return matches[0];
    }

    private sealed record Setting(string Name, string ElementName, string Value);
}
