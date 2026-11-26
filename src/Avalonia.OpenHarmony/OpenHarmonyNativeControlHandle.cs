using System.Runtime.InteropServices;
using Avalonia.Controls.Platform;
using Avalonia.Platform;

namespace Avalonia.OpenHarmony;

/// <summary>Owns an ArkUI NDK node embedded in an Avalonia <see cref="Controls.NativeControlHost"/>.</summary>
public sealed class OpenHarmonyNativeControlHandle : INativeControlHostDestroyableControlHandle
{
    private readonly Func<nint, nint> _create;
    private readonly Action<nint> _dispose;
    private nint _node;
    private int _destroyed;

    private OpenHarmonyNativeControlHandle(Func<nint, nint> create, Action<nint> dispose, Visual? owner)
    {
        _create = create;
        _dispose = dispose;
        Owner = owner;
    }

    /// <summary>The ArkUI NDK node, or zero until creation or after destruction. Access the node on ArkUI's UI thread.</summary>
    public nint Handle => Volatile.Read(ref _node);

    public string HandleDescriptor => "ArkUI_NodeHandle";

    internal Visual? Owner { get; }
    internal bool IsDestroyed => Volatile.Read(ref _destroyed) != 0;

    /// <summary>
    /// Returns the handle from <see cref="Controls.NativeControlHost.CreateNativeControlCore"/>.
    /// The factory runs on ArkUI's UI thread after the host has a surface. Return an unattached NDK node.
    /// Pass the Avalonia host as <paramref name="owner"/> to apply rectangular ancestor clips.
    /// An optional destroy callback owns node disposal and runs on ArkUI's UI thread after detachment.
    /// </summary>
    public static OpenHarmonyNativeControlHandle Create(IPlatformHandle parent, Func<nint, nint> factory,
        Action<nint>? destroy = null, Visual? owner = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (parent is not OpenHarmonyNativeControlHost.ParentHandle)
            throw new ArgumentException("An Avalonia OpenHarmony native control parent is required.", nameof(parent));
        return new(factory, destroy ?? NativeControlApi.DisposeNode, owner);
    }

    internal nint Realize(nint parent)
    {
        if (_node == 0 && !IsDestroyed)
        {
            var node = _create(parent);
            if (node == 0)
                throw new InvalidOperationException("The native control factory did not create an ArkUI node.");
            Volatile.Write(ref _node, node);
        }
        return _node;
    }

    /// <summary>Queues work on ArkUI's UI thread when the node is available.</summary>
    public bool Post(Action<nint> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return !IsDestroyed && NativeControlApi.Dispatch(() =>
        {
            if (Handle is var node && node != 0 && !IsDestroyed)
                action(node);
        });
    }

    /// <summary>Releases the node after the Avalonia attachment has been disposed.</summary>
    public void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
            return;
        NativeControlApi.Dispatch(() =>
        {
            var node = Interlocked.Exchange(ref _node, 0);
            if (node != 0)
                _dispose(node);
        });
    }
}

internal static unsafe partial class NativeControlApi
{
    private const string Bridge = "libavalonia_ohos_bridge.so";

    internal static bool Supports(long surface)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;
        try
        {
            return IsSupported(surface) != 0;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    internal static bool Dispatch(Action action, Action<string>? report = null)
    {
        var state = GCHandle.Alloc((action, report));
        if (Queue(&DispatchCallback, GCHandle.ToIntPtr(state)) != 0)
            return true;
        state.Free();
        return false;
    }

    [UnmanagedCallersOnly]
    private static void DispatchCallback(nint data, int execute)
    {
        var state = GCHandle.FromIntPtr(data);
        var (action, report) = ((Action, Action<string>?))state.Target!;
        state.Free();
        if (execute == 0)
            return;

        try
        {
            action();
        }
        catch (Exception error)
        {
            (report ?? Console.Error.WriteLine)($"OpenHarmony native control: {error.Message}");
        }
    }

    [LibraryImport(Bridge, EntryPoint = "avaoh_native_controls_supported")]
    private static partial int IsSupported(long surface);

    [LibraryImport(Bridge, EntryPoint = "avaoh_native_controls_dispatch")]
    private static partial int Queue(delegate* unmanaged<nint, int, void> callback, nint data);

    [LibraryImport(Bridge, EntryPoint = "avaoh_native_control_create_empty")]
    internal static partial nint CreateEmpty();

    [LibraryImport(Bridge, EntryPoint = "avaoh_native_control_dispose_node")]
    internal static partial void DisposeNode(nint node);

    [LibraryImport(Bridge, EntryPoint = "avaoh_native_control_create_container")]
    internal static partial nint CreateContainer(out nint parent);

    [LibraryImport(Bridge, EntryPoint = "avaoh_native_control_set_child")]
    internal static partial int SetChild(nint container, nint child);

    [LibraryImport(Bridge, EntryPoint = "avaoh_native_control_mount")]
    internal static partial int Mount(nint container, long surface);

    [LibraryImport(Bridge, EntryPoint = "avaoh_native_control_set_bounds")]
    internal static partial int SetBounds(nint container, float x, float y, float width, float height,
        float clipX, float clipY, float clipWidth, float clipHeight, int visible, int enabled);

    [LibraryImport(Bridge, EntryPoint = "avaoh_native_control_dispose_container")]
    internal static partial void DisposeContainer(nint container);
}
