using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using ProxyPlugin.Core;
using ProxyPlugin.Localization;

namespace ProxyPlugin;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public ProxySettings Settings { get; set; } = new();

    public UiLanguage Language { get; set; } = UiLanguage.Auto;

    /// <summary>
    /// Per-rule enabled overrides keyed by <see cref="MirrorRule.Id"/>. The built-in ruleset
    /// ships with default states; entries here let the user persist toggles across restarts.
    /// </summary>
    public Dictionary<string, bool> RuleOverrides { get; set; } = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
