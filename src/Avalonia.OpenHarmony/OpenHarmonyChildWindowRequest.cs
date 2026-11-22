namespace Avalonia.OpenHarmony;

internal enum OpenHarmonyChildWindowOperation
{
    Show,
    Hide,
    Close,
    Activate,
    Resize,
    Move,
    SetTitle
}

internal readonly record struct OpenHarmonyChildWindowRequest(long Id, OpenHarmonyChildWindowOperation Operation)
{
    public PixelPoint? Position { get; init; }
    public PixelSize? Size { get; init; }
    public string? Title { get; init; }
    public bool Modal { get; init; }
    public bool Activate { get; init; }
    public bool Decorated { get; init; }
}
