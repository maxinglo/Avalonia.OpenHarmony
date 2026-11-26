using Avalonia.Input;
using Avalonia.Input.Raw;

namespace Avalonia.OpenHarmony;

internal sealed partial class SurfaceTopLevel
{
    private readonly PenDevice _pen = new(releasePointerOnPenUp: true);
    private readonly Dictionary<long, RawPointerPoint> _penContacts = new();
    private const long HoverContact = -1;
    private RawPointerPoint? _penHover;

    internal void Pen(long contact, TouchPhase phase, double x, double y, RawInputModifiers modifiers,
        double pressure, double tiltX, double tiltY)
    {
        if (_inputRoot is not { } root || !double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(pressure) || !double.IsFinite(tiltX) || !double.IsFinite(tiltY))
            return;
        var point = new RawPointerPoint
        {
            Position = new Point(x / RenderScaling, y / RenderScaling),
            Pressure = (float)Math.Clamp(pressure, 0, 1),
            XTilt = (float)Math.Clamp(tiltX, -90, 90),
            YTilt = (float)Math.Clamp(tiltY, -90, 90)
        };
        modifiers &= RawInputModifiers.KeyboardMask;
        if (phase is TouchPhase.PenHover or TouchPhase.PenLeave)
        {
            if (phase == TouchPhase.PenHover && _penContacts.Count == 0)
            {
                _penHover = point;
                DispatchPen(root, HoverContact, point, RawPointerEventType.Move, modifiers);
            }
            else
            {
                LeavePenHover(root);
            }
            return;
        }

        if (phase == TouchPhase.Begin)
            LeavePenHover(root);
        else if (!_penContacts.ContainsKey(contact))
            return;

        if (phase == TouchPhase.Cancel)
        {
            _penContacts.Remove(contact);
            DispatchPen(root, contact, point, RawPointerEventType.LeaveWindow, modifiers);
            return;
        }

        var type = phase switch
        {
            TouchPhase.Begin => RawPointerEventType.LeftButtonDown,
            TouchPhase.End => RawPointerEventType.LeftButtonUp,
            _ => RawPointerEventType.Move
        };
        if (phase == TouchPhase.End)
            _penContacts.Remove(contact);
        else
        {
            _penContacts[contact] = point;
            modifiers |= RawInputModifiers.LeftMouseButton;
        }
        DispatchPen(root, contact, point, type, modifiers);
    }

    private void DispatchPen(IInputRoot root, long contact, RawPointerPoint point, RawPointerEventType type,
        RawInputModifiers modifiers) =>
        Input?.Invoke(new RawPointerEventArgs(_pen, (ulong)_clock.ElapsedMilliseconds, root, type, point, modifiers)
        {
            RawPointerId = contact
        });

    internal void CancelPenInput()
    {
        if (_penContacts.Count == 0 && _penHover is null)
            return;
        var contacts = _penContacts.ToArray();
        _penContacts.Clear();
        if (_inputRoot is { } root)
        {
            LeavePenHover(root);
            foreach (var (contact, point) in contacts)
                DispatchPen(root, contact, point, RawPointerEventType.LeaveWindow, RawInputModifiers.None);
        }
    }

    private void LeavePenHover(IInputRoot root)
    {
        if (_penHover is not { } point)
            return;
        _penHover = null;
        DispatchPen(root, HoverContact, point, RawPointerEventType.LeaveWindow, RawInputModifiers.None);
    }
}
