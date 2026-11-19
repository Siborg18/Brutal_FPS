using System.Reflection;
using System.Runtime.InteropServices;
using Brutal.GlfwApi;
using Brutal.OpenGlApi;

namespace BrutalFps.Engine;

/// <summary>
/// Loads OpenGL function pointers for the current GLFW context.
/// </summary>
public static unsafe class GlLoader
{
    // Kept alive for the lifetime of the process so the native loader never calls a collected delegate.
    private static GlGetProcAddressFunc? _procAddress;

    public static void Load()
    {
        _procAddress = name => Glfw.GetProcAddress(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(name));

        try
        {
            if (Glad.Initialize(_procAddress))
                return;

            throw new InvalidOperationException("GLAD failed to load OpenGL. Is an OpenGL 4.1 driver available?");
        }
        catch (DllNotFoundException)
        {
            // Fallback: if the native 'glad' library isn't next to the executable, load the
            // function pointers straight from GLFW. OpenGl.LoadPointers is internal, so call it by reflection.
            var loadPointers =
                typeof(OpenGl).GetMethod("LoadPointers", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find OpenGl.LoadPointers for the GLFW fallback loader.");

            Func<ReadOnlySpan<byte>, nint> loader = static name =>
            {
                // u8 string literals are null-terminated in memory, so the span can be passed straight through.
                fixed (byte* ptr = name)
                    return Glfw.GetProcAddress(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(ptr));
            };

            loadPointers.Invoke(null, [loader]);
        }
    }
}
