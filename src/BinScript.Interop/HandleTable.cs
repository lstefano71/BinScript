namespace BinScript.Interop;

using System.Collections.Concurrent;
using System.Runtime.InteropServices;

/// <summary>
/// GCHandle-based opaque handle management for C-ABI interop.
/// Maps IntPtr values to managed objects so native callers can
/// hold opaque pointers to .NET objects.
/// </summary>
public static class HandleTable
{
    private static readonly ConcurrentDictionary<IntPtr, GCHandle> _handles = new();

    /// <summary>Allocate a GCHandle for <paramref name="obj"/> and return it as IntPtr.</summary>
    public static IntPtr Alloc(object obj)
    {
        var gcHandle = GCHandle.Alloc(obj);
        var ptr = GCHandle.ToIntPtr(gcHandle);
        _handles[ptr] = gcHandle;
        return ptr;
    }

    /// <summary>Retrieve the managed object behind <paramref name="handle"/>.</summary>
    public static T? Get<T>(IntPtr handle) where T : class
    {
        if (handle == IntPtr.Zero)
            return null;

        if (_handles.TryGetValue(handle, out var gcHandle) && gcHandle.IsAllocated)
            return gcHandle.Target as T;

        return null;
    }

    /// <summary>Free the GCHandle identified by <paramref name="handle"/>.</summary>
    public static void Free(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return;

        if (_handles.TryRemove(handle, out var gcHandle) && gcHandle.IsAllocated)
            gcHandle.Free();
    }
}
