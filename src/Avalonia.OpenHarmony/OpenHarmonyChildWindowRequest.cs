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
    SetLimits
}

internal readonly record struct OpenHarmonyChildWindowRequest(long Id, OpenHarmonyChildWindowOperation Operation)
{
    public PixelPoint? Position { get; init; }
    public PixelSize? Size { get; init; }
    public string? Title { get; init; }
    public bool Modal { get; init; }
    public bool Activate { get; init; }
    public bool Decorated { get; init; }
    public bool? Resizable { get; init; }
    public OpenHarmonyWindowLimits? Limits { get; init; }
}

internal readonly record struct OpenHarmonyWindowLimits(int? MinWidth, int? MinHeight, int? MaxWidth, int? MaxHeight);
