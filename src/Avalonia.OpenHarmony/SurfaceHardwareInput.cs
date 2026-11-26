using Avalonia.Input;
using Avalonia.Input.Raw;

namespace Avalonia.OpenHarmony;

internal sealed partial class SurfaceTopLevel
{
    private const RawInputModifiers MouseButtons = RawInputModifiers.LeftMouseButton |
        RawInputModifiers.RightMouseButton | RawInputModifiers.MiddleMouseButton |
        RawInputModifiers.XButton1MouseButton | RawInputModifiers.XButton2MouseButton;

    private readonly MouseDevice _mouse = new();
    private readonly OpenHarmonyInputDiagnostics? _hardwareDiagnostics =
        AvaloniaLocator.Current.GetService<OpenHarmonyInputDiagnostics>();
    private RawInputModifiers _pressedMouseButtons;

    internal void HardwarePointer(int phase, int button, double x, double y, double wheelX, double wheelY,
        RawInputModifiers modifiers)
    {
        if (_inputRoot is not { } root || !double.IsFinite(x) || !double.IsFinite(y))
            return;

        modifiers &= RawInputModifiers.KeyboardMask | MouseButtons;
        var position = new Point(x / RenderScaling, y / RenderScaling);
        var timestamp = (ulong)_clock.ElapsedMilliseconds;

        if (phase == 4)
        {
            if (double.IsFinite(wheelX) && double.IsFinite(wheelY) && (wheelX != 0 || wheelY != 0))
                Input?.Invoke(new RawMouseWheelEventArgs(_mouse, timestamp, root, position,
                    new Vector(wheelX, wheelY), modifiers));
            return;
        }

        var pointerType = PointerTypeFor(phase, button);
        if (pointerType is not { } type)
            return;

        var buttons = modifiers & MouseButtons;
        if (type == RawPointerEventType.Move && (_pressedMouseButtons & ~buttons) != 0)
            CancelHardwarePointer();
        if (type != RawPointerEventType.LeaveWindow)
            _pressedMouseButtons = buttons;
        Input?.Invoke(new RawPointerEventArgs(_mouse, timestamp, root, type, position, modifiers));
        if (type == RawPointerEventType.LeaveWindow)
            _cursorController?.Invalidate();
        else
            _cursorController?.RefreshCustomCursor();
    }

    internal void CancelHardwarePointer()
    {
        if (_inputRoot is { } root)
        {
            var now = (ulong)_clock.ElapsedMilliseconds;
            Input?.Invoke(new RawPointerEventArgs(_mouse, now, root, RawPointerEventType.CancelCapture,
                default(Point), RawInputModifiers.None));
            Input?.Invoke(new RawPointerEventArgs(_mouse, now, root, RawPointerEventType.LeaveWindow,
                default(Point), RawInputModifiers.None));
        }
        _cursorController?.Invalidate();
        _pressedMouseButtons = RawInputModifiers.None;
    }

    internal void HardwareKey(bool down, (Key Key, PhysicalKey Physical) key, RawInputModifiers modifiers, string? text)
    {
        if (_inputRoot is not { } root)
            return;

        var timestamp = (ulong)_clock.ElapsedMilliseconds;
        var input = new RawKeyEventArgs(_keyboard, timestamp, root,
            down ? RawKeyEventType.KeyDown : RawKeyEventType.KeyUp, key.Key, modifiers, key.Physical, text);
        Input?.Invoke(input);
        if (down)
        {
            _hardwareDiagnostics?.Report($"Hardware key: handled={input.Handled}, text units={text?.Length ?? 0}");
            if (!input.Handled && text is not null)
                Input?.Invoke(new RawTextInputEventArgs(_keyboard, timestamp, root, text));
        }
    }

    private static RawPointerEventType? PointerTypeFor(int phase, int button) => (phase, button) switch
    {
        (0, _) => RawPointerEventType.Move,
        (1, 1) => RawPointerEventType.LeftButtonDown,
        (2, 1) => RawPointerEventType.LeftButtonUp,
        (1, 2) => RawPointerEventType.RightButtonDown,
        (2, 2) => RawPointerEventType.RightButtonUp,
        (1, 3) => RawPointerEventType.MiddleButtonDown,
        (2, 3) => RawPointerEventType.MiddleButtonUp,
        (1, 4) => RawPointerEventType.XButton1Down,
        (2, 4) => RawPointerEventType.XButton1Up,
        (1, 5) => RawPointerEventType.XButton2Down,
        (2, 5) => RawPointerEventType.XButton2Up,
        (3, _) => RawPointerEventType.LeaveWindow,
        _ => null
    };
}
