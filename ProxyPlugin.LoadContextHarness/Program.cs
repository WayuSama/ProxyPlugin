using System.Reflection;
using System.Runtime.Loader;

var folder = Path.GetFullPath(args[0]);
var harmonyPath = Path.Combine(folder, "0Harmony.dll");
if (args.Contains("--legacy-default"))
    AssemblyLoadContext.Default.LoadFromAssemblyPath(harmonyPath);

// Model Dalamud's isolated non-collectible host and collectible plugin contexts.
var host = new AssemblyLoadContext("Dalamud-like host", isCollectible: false);
var harmony = host.LoadFromAssemblyPath(harmonyPath);
var plugin = new AssemblyLoadContext("Dalamud-like plugin", isCollectible: true);
plugin.Resolving += (_, name) => name.Name == "ProxyPlugin.Core"
    ? plugin.LoadFromAssemblyPath(Path.Combine(folder, "ProxyPlugin.Core.dll")) : null;

using (plugin.EnterContextualReflection())
{
    var assembly = plugin.LoadFromAssemblyPath(Path.Combine(folder, "ProxyPlugin.Harness.dll"));
    var run = assembly.GetType("Program")!.GetMethod("Run")!;
    var exitCode = await (Task<int>)run.Invoke(null, new object[] { harmony })!;
    if (exitCode != 0)
        return exitCode;
    if (assembly.GetReferencedAssemblies().Any(x => x.Name == "0Harmony"))
        throw new Exception("Plugin has a direct Harmony reference: load-context isolation is broken.");
}
plugin.Unload();
Console.WriteLine("PASS isolated host + collectible plugin" + (args.Contains("--legacy-default") ? " + legacy Default Harmony" : ""));
return 0;
