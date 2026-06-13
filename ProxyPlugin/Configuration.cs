using System;
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

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
