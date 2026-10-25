using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;

namespace ProxyPlugin;

/// <summary>
/// Binds to the host's Harmony without introducing a second merged MonoMod.
/// Dalamud lives in an isolated, non-collectible context, not Default.
/// No Harmony references are allowed in this assembly: the plugin loader could
/// otherwise bind them to an incompatible copy in Default.
/// </summary>
public sealed class HarmonyRuntime
{
    private readonly object _harmony;
    private readonly Type _harmonyMethod;
    private readonly MethodInfo _patch;
    private readonly MethodInfo _unpatch;
    private readonly AssemblyLoadContext _context;
    private readonly string _id;

    public HarmonyRuntime(Assembly harmonyAssembly, string id)
    {
        _context = AssemblyLoadContext.GetLoadContext(harmonyAssembly)!;
        if (_context.IsCollectible)
            throw new InvalidOperationException("Harmony must belong to the non-collectible host context.");
        EnsureProxyIdentity(harmonyAssembly);
        _id = id;
        var harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony", throwOnError: true)!;
        _harmonyMethod = harmonyAssembly.GetType("HarmonyLib.HarmonyMethod", throwOnError: true)!;
        _harmony = Activator.CreateInstance(harmonyType, id)!;
        _patch = harmonyType.GetMethod("Patch", new[] { typeof(MethodBase), _harmonyMethod, _harmonyMethod, _harmonyMethod, _harmonyMethod })!;
        _unpatch = harmonyType.GetMethod("UnpatchAll", new[] { typeof(string) })!;
    }

    // Older ProxyPlugin builds loaded another Harmony into Default. Its generated
    // ILGeneratorProxy may remain cached for the rest of the process lifetime.
    // A warm upgrade can reuse the already generated proxy whose generic constraint
    // matches this host, rather than retaining a helper bound to the other copy.
    private static void EnsureProxyIdentity(Assembly harmonyAssembly)
    {
        var shim = harmonyAssembly.GetType("MonoMod.Utils.Cil.ILGeneratorShim", true)!;
        var proxyType = (Type)shim.GetProperty("GenericProxyType", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
        bool Matches(Type type) => type.IsGenericTypeDefinition
            && type.GetGenericArguments()[0].GetGenericParameterConstraints() is { Length: 1 } constraints
            && constraints[0] == shim;
        if (Matches(proxyType))
            return;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var candidate = assembly.GetType("MonoMod.Utils.Cil.ILGeneratorProxy");
            if (candidate == null || !Matches(candidate))
                continue;
            var cache = shim.GetNestedType("ILGeneratorBuilder", BindingFlags.NonPublic)!
                .GetField("ProxyType", BindingFlags.NonPublic | BindingFlags.Static)!;
            cache.SetValue(null, candidate);
            return;
        }
        throw new InvalidOperationException("An incompatible Harmony helper remains from an older plugin. Restart the game to clear it.");
    }

    public static HarmonyRuntime FromHost(Assembly hostAssembly, string id)
    {
        var context = AssemblyLoadContext.GetLoadContext(hostAssembly)!;
        var harmony = context.LoadFromAssemblyName(new AssemblyName("0Harmony"));
        var shim = harmony.GetType("MonoMod.Utils.Cil.ILGeneratorShim", true)!;
        var proxy = (Type)shim.GetProperty("GenericProxyType", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
        var boundAssembly = proxy.GetGenericArguments()[0].GetGenericParameterConstraints()[0].Assembly;
        if (boundAssembly != harmony && boundAssembly.GetName().Name == "0Harmony")
            harmony = boundAssembly;
        return new HarmonyRuntime(harmony, id);
    }

    public IDisposable EnterContext() => _context.EnterContextualReflection();

    public void Patch(MethodInfo original, MethodInfo? prefix = null, MethodInfo? postfix = null)
    {
        object? Wrap(MethodInfo? method) => method == null ? null : Activator.CreateInstance(_harmonyMethod, method);
        Invoke(_patch, new[] { original, Wrap(prefix), Wrap(postfix), null, null });
    }

    public void Unpatch() => Invoke(_unpatch, new object?[] { _id });

    private void Invoke(MethodInfo method, object?[] arguments)
    {
        try { method.Invoke(_harmony, arguments); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }
}

