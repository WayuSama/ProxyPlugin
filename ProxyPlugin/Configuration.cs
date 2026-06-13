using System;
using Dalamud.Configuration;
using ProxyPlugin.Core;

namespace ProxyPlugin;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public ProxySettings Settings { get; set; } = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
