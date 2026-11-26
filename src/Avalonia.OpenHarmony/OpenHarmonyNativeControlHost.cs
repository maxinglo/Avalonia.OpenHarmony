using Avalonia.Controls.Platform;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace Avalonia.OpenHarmony;

internal sealed class OpenHarmonyNativeControlHost(SurfaceTopLevel topLevel, Action<string> reportError)
    : INativeControlHostImpl, IDisposable
{
    private readonly SurfaceTopLevel _topLevel = topLevel;
    private readonly HashSet<Attachment> _attachments = [];
    private long _surface;
    private bool _enabled = true;
    private bool _disposed;

    internal void SetSurface(long surface)
    {
        if (_surface == surface)
            return;
        _surface = surface;
        RefreshAttachments();
    }

    internal void SetInputEnabled(bool enabled)
    {
        if (_enabled == enabled)
            return;
        _enabled = enabled;
        RefreshAttachments();
    }

    internal void RefreshAttachments()
    {
        foreach (var attachment in _attachments)
            attachment.Update();
    }

    public INativeControlHostDestroyableControlHandle CreateDefaultChild(IPlatformHandle parent) =>
        OpenHarmonyNativeControlHandle.Create(parent, _ => NativeControlApi.CreateEmpty());

    public INativeControlHostControlTopLevelAttachment CreateNewAttachment(Func<IPlatformHandle, IPlatformHandle> create) =>
        CreateNewAttachment(create(new ParentHandle()));

    public INativeControlHostControlTopLevelAttachment CreateNewAttachment(IPlatformHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (handle is not OpenHarmonyNativeControlHandle { IsDestroyed: false } child)
            throw new ArgumentException("Use OpenHarmonyNativeControlHandle.Create to wrap a native ArkUI node.", nameof(handle));
        return new Attachment(child, reportError) { AttachedTo = this };
    }

    public bool IsCompatibleWith(IPlatformHandle handle) => handle is OpenHarmonyNativeControlHandle { IsDestroyed: false };

    public void Dispose()
    {
        _disposed = true;
        while (_attachments.Count != 0)
            _attachments.First().AttachedTo = null;
    }

    internal sealed class ParentHandle : IPlatformHandle
    {
        public nint Handle => 0;
        public string HandleDescriptor => "ArkUI native control parent";
    }

    private sealed class Attachment(OpenHarmonyNativeControlHandle child, Action<string> reportError)
        : INativeControlHostControlTopLevelAttachment
    {
        private OpenHarmonyNativeControlHost? _attachedTo;
        private Rect _bounds;
        private bool _visible;
        private bool _disposed;
        private NativeLayout? _queued;

        // ArkUI owns the node tree. Access the native container only from the ArkUI dispatcher.
        private nint _container;

        public INativeControlHostImpl? AttachedTo
        {
            get => _attachedTo;
            set
            {
                if (ReferenceEquals(_attachedTo, value))
                    return;
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (value != null && !IsCompatibleWith(value))
                    throw new ArgumentException("An active OpenHarmony native control host is required.", nameof(value));
                _attachedTo?._attachments.Remove(this);
                _attachedTo = (OpenHarmonyNativeControlHost?)value;
                _attachedTo?._attachments.Add(this);
                _visible = false;
                Update();
            }
        }

        public bool IsCompatibleWith(INativeControlHostImpl host) => host is OpenHarmonyNativeControlHost { _disposed: false };

        public void HideWithSize(Size size)
        {
            _bounds = new Rect(size);
            _visible = false;
            Update();
        }

        public void ShowInBounds(Rect rect)
        {
            _bounds = rect;
            _visible = true;
            Update();
        }

        public void Update()
        {
            var host = _attachedTo;
            var surface = host?._surface ?? 0;
            var bounds = IsFinite(_bounds) ? _bounds : default;
            var clip = _visible && host != null ? GetClip(child.Owner, host, bounds) : default;
            var layout = new NativeLayout(surface, bounds, clip, host?._enabled ?? false);
            if (layout == _queued)
                return;
            if (NativeControlApi.Dispatch(() => Apply(layout), reportError))
                _queued = layout;
        }

        private static bool IsFinite(Rect rect) => double.IsFinite(rect.X) && double.IsFinite(rect.Y) &&
            double.IsFinite(rect.Width) && double.IsFinite(rect.Height) && rect.Width >= 0 && rect.Height >= 0;

        private static Rect GetClip(Visual? owner, OpenHarmonyNativeControlHost host, Rect bounds)
        {
            var clip = bounds.Intersect(new Rect(host._topLevel.ClientSize));
            if (owner?.GetPresentationSource()?.RootVisual is not Visual root)
                return clip;

            for (Visual? current = owner; current != null; current = current.GetVisualParent())
            {
                if (!current.ClipToBounds && current.Clip is null)
                    continue;
                if (current.TransformToVisual(root) is not { } transform)
                    return default;
                if (current.ClipToBounds)
                    clip = clip.Intersect(new Rect(current.Bounds.Size).TransformToAABB(transform));
                if (current.Clip is { } geometry)
                    clip = clip.Intersect(geometry.Bounds.TransformToAABB(transform));
            }
            return clip;
        }

        private void Apply(NativeLayout layout)
        {
            if (layout.Surface == 0)
            {
                if (_container != 0)
                    NativeControlApi.Mount(_container, 0);
                return;
            }
            if (!NativeControlApi.Supports(layout.Surface) || child.IsDestroyed)
                return;

            if (_container == 0)
            {
                var container = NativeControlApi.CreateContainer(out var parent);
                if (container == 0)
                    throw new InvalidOperationException("Could not create an ArkUI native control container.");
                try
                {
                    if (NativeControlApi.Mount(container, layout.Surface) == 0 ||
                        NativeControlApi.SetChild(container, child.Realize(parent)) == 0)
                        throw new InvalidOperationException("Could not attach the ArkUI node to its native control slot.");
                    _container = container;
                }
                catch
                {
                    NativeControlApi.DisposeContainer(container);
                    throw;
                }
            }
            else if (NativeControlApi.Mount(_container, layout.Surface) == 0)
                throw new InvalidOperationException("Could not move the ArkUI native control to its current surface.");

            var bounds = layout.Bounds;
            var clip = layout.Clip;
            if (NativeControlApi.SetBounds(_container, (float)bounds.X, (float)bounds.Y, (float)bounds.Width,
                    (float)bounds.Height, (float)clip.X, (float)clip.Y, (float)clip.Width, (float)clip.Height,
                    clip.Width > 0 && clip.Height > 0 ? 1 : 0, layout.Enabled ? 1 : 0) == 0)
                throw new InvalidOperationException("Could not set the ArkUI native control geometry.");
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            AttachedTo = null;
            _disposed = true;
            NativeControlApi.Dispatch(() =>
            {
                if (_container != 0)
                    NativeControlApi.DisposeContainer(_container);
                _container = 0;
            }, reportError);
        }

        private readonly record struct NativeLayout(long Surface, Rect Bounds, Rect Clip, bool Enabled);
    }
}
