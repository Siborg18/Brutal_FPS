using System.Reflection;
using System.Runtime.CompilerServices;

namespace BrutalFps.Engine;

/// <summary>
/// Compiles the game's own methods on background threads while it loads. Otherwise each one is compiled by the JIT the first
/// time it runs: on the game's thread, as a hitch of up to 20 ms in the middle of play (the first shot, the first
/// ragdoll, the first NPC to see you), and all through loading. (The first frames after this still aren't fully
/// optimised: the runtime does that later by itself, in the background.)
/// </summary>
public static class Precompile
{
    public static void Start()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "Precompile", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    private static void Run()
    {
        const BindingFlags all = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var types = typeof(Precompile).Assembly.GetTypes();
        Parallel.ForEach(types, new ParallelOptions { MaxDegreeOfParallelism = int.Max(Environment.ProcessorCount / 2, 1) }, type =>
        {
            if (type.ContainsGenericParameters)
                return;
            foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                if (method.IsAbstract || method.ContainsGenericParameters || method.IsStatic && method.IsConstructor)
                    continue;
                try
                {
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
                }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or TypeLoadException or BadImageFormatException or NotSupportedException or TypeInitializationException)
                {
                    // (Not a method that can be compiled on its own: it just waits for its first call, as usual.)
                }
            }
        });
    }
}
