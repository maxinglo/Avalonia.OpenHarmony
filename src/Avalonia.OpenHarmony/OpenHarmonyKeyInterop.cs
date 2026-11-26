using Avalonia.Input;

namespace Avalonia.OpenHarmony;

internal static class OpenHarmonyKeyInterop
{
    // The codes and contiguous ranges come from @ohos.multimodalInput.keyCode in the API 26 SDK.
    internal static (Key Key, PhysicalKey Physical) Map(int keyCode, bool numLock) => keyCode switch
    {
        >= 2000 and <= 2009 => (Key.D0 + (keyCode - 2000), PhysicalKey.Digit0 + (keyCode - 2000)),
        >= 2017 and <= 2042 => (Key.A + (keyCode - 2017), PhysicalKey.A + (keyCode - 2017)),
        >= 2090 and <= 2101 => (Key.F1 + (keyCode - 2090), PhysicalKey.F1 + (keyCode - 2090)),
        >= 2816 and <= 2827 => (Key.F13 + (keyCode - 2816), PhysicalKey.F13 + (keyCode - 2816)),
        >= 2103 and <= 2112 => (numLock ? Key.NumPad0 + (keyCode - 2103) : KeypadNavigation(keyCode),
            PhysicalKey.NumPad0 + (keyCode - 2103)),
        10 or 2630 => (Key.MediaPlayPause, PhysicalKey.MediaPlayPause),
        11 => (Key.MediaStop, PhysicalKey.MediaStop),
        12 => (Key.MediaNextTrack, PhysicalKey.MediaTrackNext),
        13 => (Key.MediaPreviousTrack, PhysicalKey.MediaTrackPrevious),
        16 => (Key.VolumeUp, PhysicalKey.AudioVolumeUp),
        17 => (Key.VolumeDown, PhysicalKey.AudioVolumeDown),
        22 => (Key.VolumeMute, PhysicalKey.AudioVolumeMute),
        2012 => (Key.Up, PhysicalKey.ArrowUp),
        2013 => (Key.Down, PhysicalKey.ArrowDown),
        2014 => (Key.Left, PhysicalKey.ArrowLeft),
        2015 => (Key.Right, PhysicalKey.ArrowRight),
        2043 => (Key.OemComma, PhysicalKey.Comma),
        2044 => (Key.OemPeriod, PhysicalKey.Period),
        2045 => (Key.LeftAlt, PhysicalKey.AltLeft),
        2046 => (Key.RightAlt, PhysicalKey.AltRight),
        2047 => (Key.LeftShift, PhysicalKey.ShiftLeft),
        2048 => (Key.RightShift, PhysicalKey.ShiftRight),
        2049 => (Key.Tab, PhysicalKey.Tab),
        2050 => (Key.Space, PhysicalKey.Space),
        2054 => (Key.Enter, PhysicalKey.Enter),
        2055 => (Key.Back, PhysicalKey.Backspace),
        2056 => (Key.OemTilde, PhysicalKey.Backquote),
        2057 => (Key.OemMinus, PhysicalKey.Minus),
        2058 => (Key.OemPlus, PhysicalKey.Equal),
        2059 => (Key.OemOpenBrackets, PhysicalKey.BracketLeft),
        2060 => (Key.OemCloseBrackets, PhysicalKey.BracketRight),
        2061 => (Key.OemPipe, PhysicalKey.Backslash),
        2062 => (Key.OemSemicolon, PhysicalKey.Semicolon),
        2063 => (Key.OemQuotes, PhysicalKey.Quote),
        2064 => (Key.OemQuestion, PhysicalKey.Slash),
        2067 => (Key.Apps, PhysicalKey.ContextMenu),
        2068 => (Key.PageUp, PhysicalKey.PageUp),
        2069 => (Key.PageDown, PhysicalKey.PageDown),
        2070 => (Key.Escape, PhysicalKey.Escape),
        2071 => (Key.Delete, PhysicalKey.Delete),
        2072 => (Key.LeftCtrl, PhysicalKey.ControlLeft),
        2073 => (Key.RightCtrl, PhysicalKey.ControlRight),
        2074 => (Key.CapsLock, PhysicalKey.CapsLock),
        2075 => (Key.Scroll, PhysicalKey.ScrollLock),
        2076 => (Key.LWin, PhysicalKey.MetaLeft),
        2077 => (Key.RWin, PhysicalKey.MetaRight),
        2079 => (Key.PrintScreen, PhysicalKey.PrintScreen),
        2080 => (Key.Pause, PhysicalKey.Pause),
        2081 => (Key.Home, PhysicalKey.Home),
        2082 => (Key.End, PhysicalKey.End),
        2083 => (Key.Insert, PhysicalKey.Insert),
        2102 => (Key.NumLock, PhysicalKey.NumLock),
        2113 => (Key.Divide, PhysicalKey.NumPadDivide),
        2114 => (Key.Multiply, PhysicalKey.NumPadMultiply),
        2115 => (Key.Subtract, PhysicalKey.NumPadSubtract),
        2116 => (Key.Add, PhysicalKey.NumPadAdd),
        2117 => (numLock ? Key.Decimal : Key.Delete, PhysicalKey.NumPadDecimal),
        2118 => (Key.Separator, PhysicalKey.NumPadComma),
        2119 => (Key.Enter, PhysicalKey.NumPadEnter),
        2120 => (Key.OemPlus, PhysicalKey.NumPadEqual),
        _ => default
    };

    internal static string? Text((Key Key, PhysicalKey Physical) key, int unicode, RawInputModifiers modifiers)
    {
        const RawInputModifiers shortcutModifiers =
            RawInputModifiers.Alt | RawInputModifiers.Control | RawInputModifiers.Meta;
        if ((modifiers & shortcutModifiers) != 0)
            return null;
        var keypadNavigation = key.Physical is >= PhysicalKey.NumPad0 and <= PhysicalKey.NumPad9 &&
            key.Key is < Key.NumPad0 or > Key.NumPad9;
        if (keypadNavigation || (key.Physical == PhysicalKey.NumPadDecimal && key.Key == Key.Delete))
            return null;

        // ArkUI's hardware-key Unicode value covers printable basic Latin. Space is a separate key.
        return unicode is >= 0x20 and <= 0x7E ? ((char)unicode).ToString() :
            key.Key == Key.Space ? " " : null;
    }

    private static Key KeypadNavigation(int code) => code switch
    {
        2103 => Key.Insert,
        2104 => Key.End,
        2105 => Key.Down,
        2106 => Key.PageDown,
        2107 => Key.Left,
        2108 => Key.Clear,
        2109 => Key.Right,
        2110 => Key.Home,
        2111 => Key.Up,
        2112 => Key.PageUp,
        _ => Key.None
    };
}
