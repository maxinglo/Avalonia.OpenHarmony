using Avalonia.Controls;
using Avalonia.Platform;

namespace Avalonia.OpenHarmony;

internal enum OpenHarmonyChildWindowOperation
{
    Show,
    Hide,
    Close,
    Activate,
    Resize,
    Move,
    SetTitle,
    SetDecorations,
    SetResizable,
    SetLimits,
    SetState,
    SetCaptionButtons,
    SetExtendedTitleBar
}

internal readonly record struct OpenHarmonyChildWindowRequest(long Id, OpenHarmonyChildWindowOperation Operation)
{
    public PixelPoint? Position { get; init; }
    public PixelSize? Size { get; init; }
    public PixelSize? StageClientSize { get; init; }
    public string? Title { get; init; }
    public bool Modal { get; init; }
    public bool Activate { get; init; }
    public bool Decorated { get; init; }
    public bool? Resizable { get; init; }
    public OpenHarmonyWindowLimits? Limits { get; init; }
    public WindowState? State { get; init; }
    public int StateRequestVersion { get; init; }
    public bool CanMinimize { get; init; }
    public bool CanMaximize { get; init; }
    public bool ExtendTitleBar { get; init; }
    public double TitleBarHeight { get; init; }
    public PlatformThemeVariant? TitleBarTheme { get; init; }
}

internal readonly record struct OpenHarmonyWindowLimits(int? MinWidth, int? MinHeight, int? MaxWidth, int? MaxHeight);
