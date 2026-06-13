using System;
using System.Resources;

namespace ProxyPlugin.Localization;

/// <summary>
/// Minimal localization facade over the embedded <c>Strings</c> / <c>StringsZh</c> resource files.
/// Both languages are embedded in the main assembly (no satellite assemblies, so Dalamud packaging
/// ships a single DLL); <see cref="Apply"/> selects the active table from the configured preference
/// (resolving <see cref="UiLanguage.Auto"/> against Dalamud's UI language). Missing keys fall back to
/// English, then to the key itself, so the UI never renders blank.
/// </summary>
internal static class Loc
{
    private static readonly ResourceManager EnResources = CreateManager("Strings");
    private static readonly ResourceManager ZhResources = CreateManager("StringsZh");

    private static ResourceManager _active = EnResources;

    /// <summary>The language currently in effect (never <see cref="UiLanguage.Auto"/>).</summary>
    public static UiLanguage Resolved { get; private set; } = UiLanguage.English;

    public static void Apply(UiLanguage configured, string? dalamudUiLanguage)
    {
        Resolved = configured == UiLanguage.Auto ? Detect(dalamudUiLanguage) : configured;
        _active = Resolved == UiLanguage.ChineseSimplified ? ZhResources : EnResources;
    }

    public static string T(string key)
        => _active.GetString(key) ?? EnResources.GetString(key) ?? key;

    public static string T(string key, params object[] args)
        => string.Format(T(key), args);

    private static UiLanguage Detect(string? langCode)
        => !string.IsNullOrEmpty(langCode) && langCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.ChineseSimplified
            : UiLanguage.English;

    private static ResourceManager CreateManager(string stem)
    {
        // Resolve the real manifest name (e.g. "ProxyPlugin.Resources.Strings") instead of hard-coding
        // the root namespace, so the lookup survives any namespace/layout change.
        var assembly = typeof(Loc).Assembly;
        var suffix = "." + stem + ".resources";
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
                return new ResourceManager(name[..^".resources".Length], assembly);
        }

        // Fall back to the conventional name; GetString will then return null and callers see the key.
        return new ResourceManager("ProxyPlugin.Resources." + stem, assembly);
    }
}
