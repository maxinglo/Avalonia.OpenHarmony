using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Platform;

namespace Avalonia.OpenHarmony;

internal sealed class OpenHarmonyCursor(StandardCursorType kind) : ICursorImpl
{
    public StandardCursorType Kind { get; } = kind;

    public void Dispose() { }
}

internal sealed partial class OpenHarmonyCursorController(Func<int> getWindowId)
{
    private (int Window, PointerStyle Style) _last;
    private bool _nativeLibraryUnavailable;

    public void Apply(StandardCursorType kind)
    {
        if (_nativeLibraryUnavailable)
            return;
        var window = getWindowId();
        if (window <= 0)
            return;
        var style = Map(kind);
        if (_last == (window, style))
            return;
        try
        {
            if (SetPointerStyle(window, style) == 0)
                _last = (window, style);
        }
        catch (DllNotFoundException)
        {
            _nativeLibraryUnavailable = true;
        }
        catch (EntryPointNotFoundException)
        {
            _nativeLibraryUnavailable = true;
        }
    }

    private static PointerStyle Map(StandardCursorType kind) => kind switch
    {
        StandardCursorType.Ibeam => PointerStyle.Text,
        StandardCursorType.Wait => PointerStyle.Loading,
        StandardCursorType.Cross => PointerStyle.Cross,
        StandardCursorType.UpArrow or StandardCursorType.TopSide => PointerStyle.North,
        StandardCursorType.SizeWestEast => PointerStyle.WestEast,
        StandardCursorType.SizeNorthSouth => PointerStyle.NorthSouth,
        StandardCursorType.SizeAll or StandardCursorType.DragMove => PointerStyle.Move,
        StandardCursorType.No => PointerStyle.Forbidden,
        StandardCursorType.Hand or StandardCursorType.DragLink => PointerStyle.PointingHand,
        StandardCursorType.AppStarting => PointerStyle.Running,
        StandardCursorType.Help => PointerStyle.Help,
        StandardCursorType.BottomSide => PointerStyle.South,
        StandardCursorType.LeftSide => PointerStyle.West,
        StandardCursorType.RightSide => PointerStyle.East,
        StandardCursorType.TopLeftCorner => PointerStyle.NorthWest,
        StandardCursorType.TopRightCorner => PointerStyle.NorthEast,
        StandardCursorType.BottomLeftCorner => PointerStyle.SouthWest,
        StandardCursorType.BottomRightCorner => PointerStyle.SouthEast,
        StandardCursorType.DragCopy => PointerStyle.Copy,
        _ => PointerStyle.Default
    };

    // Values match the SDK's Input_PointerStyle in oh_pointer_style.h.
    private enum PointerStyle
    {
        Default = 0, East = 1, West = 2, South = 3, North = 4,
        WestEast = 5, NorthSouth = 6, NorthEast = 7, NorthWest = 8, SouthEast = 9, SouthWest = 10,
        Cross = 13, Copy = 14, Forbidden = 15, PointingHand = 19, Help = 20, Move = 21,
        Text = 26, Loading = 42, Running = 43
    }

    [LibraryImport("libohinput.so", EntryPoint = "OH_Input_SetPointerStyle")]
    private static partial int SetPointerStyle(int windowId, PointerStyle style);
}
