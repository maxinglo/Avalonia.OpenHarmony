using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Avalonia.OpenHarmony;

/// <summary>Invokes an ArkTS handler registered for the current application window.</summary>
public static partial class OpenHarmonySystemRequests
{
    private static readonly ConcurrentDictionary<long, TaskCompletionSource<OpenHarmonySystemResult>> Pending = new();
    private static long s_nextId;

    public static unsafe Task<OpenHarmonySystemResult> RequestAsync(string operation, string argument)
    {
        ArgumentException.ThrowIfNullOrEmpty(operation);
        ArgumentNullException.ThrowIfNull(argument);
        var id = Interlocked.Increment(ref s_nextId);
        var completion = new TaskCompletionSource<OpenHarmonySystemResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Pending[id] = completion;
        try
        {
            if (Request(id, operation, argument, &Complete) == 0)
                CompleteWithoutResult(id);
        }
        catch
        {
            Pending.TryRemove(id, out _);
            throw;
        }
        return completion.Task;
    }

    private static void CompleteWithoutResult(long id)
    {
        if (Pending.TryRemove(id, out var completion))
            completion.TrySetResult(default);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Complete(long id, nint value, int fd)
    {
        var file = fd < 0 ? null : new SafeFileHandle(fd, ownsHandle: true);
        if (Pending.TryRemove(id, out var completion))
            completion.TrySetResult(new(Marshal.PtrToStringUTF8(value), file));
        else
            file?.Dispose();
    }

    [LibraryImport("libavalonia_ohos_bridge.so", EntryPoint = "avaoh_system_request",
        StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial int Request(long id, string operation, string argument,
        delegate* unmanaged[Cdecl]<long, nint, int, void> complete);
}
