using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Avalonia.OpenHarmony;

/// <summary>A HarmonyOS display in the desktop's physical-pixel coordinate space.</summary>
public readonly record struct OpenHarmonyDisplayInfo(long Id, string? Name, PixelRect Bounds, PixelRect WorkingArea,
    double Scaling, ScreenOrientation Orientation, bool IsPrimary);

public sealed partial class OpenHarmonyHost
{
    /// <summary>Copies the display array carried by the bridge's avaoh_set_displays callback.</summary>
    /// <remarks>The native pointers only need to remain valid until this method returns.</remarks>
    public unsafe void UpdateNativeDisplays(nint displays, int count)
    {
        if (count < 0 || count > 256 || (count != 0 && displays == 0))
            return;
        var input = new ReadOnlySpan<NativeDisplay>((void*)displays, count);
        var snapshot = new OpenHarmonyDisplayInfo[count];
        for (var index = 0; index < count; index++)
        {
            var item = input[index];
            snapshot[index] = new OpenHarmonyDisplayInfo(item.Id, Marshal.PtrToStringUTF8(item.Name),
                new PixelRect(item.Left, item.Top, item.Width, item.Height),
                new PixelRect(item.WorkLeft, item.WorkTop, item.WorkWidth, item.WorkHeight),
                item.Scaling, item.Orientation, item.Primary != 0);
        }
        UpdateDisplays(snapshot);
    }

    // Keep this in sync with DisplaySnapshot in platform_displays.h.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeDisplay
    {
        public long Id;
        public nint Name;
        public double Scaling;
        public int Left, Top, Width, Height;
        public int WorkLeft, WorkTop, WorkWidth, WorkHeight;
        public ScreenOrientation Orientation;
        public int Primary;
    }
}

internal sealed class OpenHarmonyScreens : IScreenImpl
{
    private readonly Dictionary<long, DisplayScreen> _screens = new();
    private IReadOnlyList<Screen> _all = Array.Empty<Screen>();
    private long? _hostDisplay;

    public int ScreenCount => _all.Count;
    public IReadOnlyList<Screen> AllScreens => _all;
    public Action? Changed { get; set; }
    public PixelPoint HostOrigin => HostScreen?.Bounds.Position ?? default;
    public PixelPoint OriginOfDisplay(long id) => _screens.TryGetValue(id, out var screen)
        ? screen.Bounds.Position
        : default;

    private Screen? HostScreen => _hostDisplay is { } id && _screens.TryGetValue(id, out var screen)
        ? screen
        : _all.FirstOrDefault(screen => screen.IsPrimary);

    public bool Replace(OpenHarmonyDisplayInfo[] displays)
    {
        var screens = new List<Screen>(displays.Length);
        var indexed = new HashSet<long>();
        var updated = false;
        foreach (var display in displays)
        {
            if (display.Id < 0 || display.Bounds.Width <= 0 || display.Bounds.Height <= 0
                || !double.IsFinite(display.Scaling) || display.Scaling <= 0 || !indexed.Add(display.Id))
                continue;
            if (!_screens.TryGetValue(display.Id, out var screen))
            {
                screen = new DisplayScreen(display.Id);
                _screens.Add(display.Id, screen);
                updated = true;
            }
            updated |= screen.Update(display);
            screens.Add(screen);
        }
        foreach (var id in _screens.Keys.Where(id => !indexed.Contains(id)).ToArray())
        {
            _screens[id].Clear();
            _screens.Remove(id);
            updated = true;
        }
        if (updated || !_all.SequenceEqual(screens))
        {
            _all = screens.AsReadOnly();
            return true;
        }
        return false;
    }

    public bool SetHostDisplay(long id)
    {
        if (_hostDisplay == id)
            return false;
        _hostDisplay = id;
        return true;
    }

    public void NotifyChanged() => Changed?.Invoke();

    public Screen? ScreenFromWindow(IWindowBaseImpl window) => window is OpenHarmonyChildWindow child
        ? ScreenFromRect(new PixelRect(child.Position,
            PixelSize.FromSize(child.FrameSize ?? child.ClientSize, child.DesktopScaling))) ?? HostScreen
        : null;
    public Screen? ScreenFromTopLevel(ITopLevelImpl topLevel) => topLevel switch
    {
        IWindowBaseImpl window => ScreenFromWindow(window),
        SurfaceTopLevel => HostScreen,
        _ => null
    };
    public Screen? ScreenFromPoint(PixelPoint point) =>
        _all.FirstOrDefault(screen => screen.Bounds.ContainsExclusive(point));
    public Screen? ScreenFromRect(PixelRect rect) => _all
        .Select(screen => (Screen: screen, Area: screen.Bounds.Intersect(rect)))
        .Where(candidate => candidate.Area.Width > 0 && candidate.Area.Height > 0)
        .OrderByDescending(candidate => (long)candidate.Area.Width * candidate.Area.Height)
        .Select(candidate => candidate.Screen)
        .FirstOrDefault();
    public Task<bool> RequestScreenDetails() => Task.FromResult(_all.Count != 0);

    private sealed class DisplayScreen(long id) : PlatformScreen(new PlatformHandle((nint)id, "HarmonyOS display ID"))
    {
        private OpenHarmonyDisplayInfo? _info;

        public bool Update(OpenHarmonyDisplayInfo info)
        {
            if (_info == info)
                return false;
            _info = info;
            DisplayName = info.Name;
            CurrentOrientation = info.Orientation;
            Scaling = info.Scaling;
            Bounds = info.Bounds;
            WorkingArea = info.WorkingArea.Width > 0 && info.WorkingArea.Height > 0 ? info.WorkingArea : info.Bounds;
            IsPrimary = info.IsPrimary;
            return true;
        }

        public void Clear()
        {
            DisplayName = null;
            CurrentOrientation = ScreenOrientation.None;
            Scaling = 0;
            Bounds = WorkingArea = default;
            IsPrimary = false;
        }
    }
}
