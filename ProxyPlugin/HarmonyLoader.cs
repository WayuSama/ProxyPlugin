using System;
using System.IO;
using System.Runtime.Loader;

namespace ProxyPlugin;

/// <summary>
/// Hosts <c>0Harmony.dll</c> in the process-wide, non-collectible Default <see cref="AssemblyLoadContext"/>.
/// </summary>
/// <remarks>
/// Dalamud loads every plugin into a collectible ALC, but MonoMod (Harmony's detour engine) cannot
/// operate from a collectible context: the runtime forbids resolving a collectible assembly from a
/// non-collectible one, which surfaces as
/// <c>FileLoadException: Could not load file or assembly '0Harmony' ... Resolving to a collectible
/// assembly is not supported</c>.
///
/// The plugin therefore ships <c>0Harmony.dll</c> under <c>libs\</c> (kept out of <c>deps.json</c> and
/// out of the plugin base directory) and loads it into the Default ALC here. Dalamud's per-plugin
/// loader, finding no <c>0Harmony</c> in either probe location, falls back to the Default context and
/// unifies on this copy — so all Harmony/MonoMod work then runs non-collectibly.
///
/// This type deliberately references no <c>HarmonyLib</c> symbols, so JIT-compiling it never triggers
/// the very assembly load it is meant to set up. <see cref="EnsureLoaded"/> must run before any code
/// that touches a Harmony type (i.e. before <see cref="HttpInterceptor"/> is constructed).
/// </remarks>
internal static class HarmonyLoader
{
    private const string HarmonyAssemblyName = "0Harmony";

    private static bool _ensured;

    public static void EnsureLoaded(string pluginDirectory)
    {
        if (_ensured)
            return;

        // Another plugin (or a previous load of this one) may have already hosted Harmony in the
        // long-lived Default context; never load a second copy.
        foreach (var asm in AssemblyLoadContext.Default.Assemblies)
        {
            if (string.Equals(asm.GetName().Name, HarmonyAssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                _ensured = true;
                return;
            }
        }

        var path = Path.Combine(pluginDirectory, "libs", HarmonyAssemblyName + ".dll");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"ProxyPlugin: bundled '{HarmonyAssemblyName}.dll' was not found at '{path}'. The plugin package is incomplete.");

        AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        _ensured = true;
    }
}
