namespace Avalonia.OpenHarmony;

/// <summary>Configures rendering for OpenHarmony XComponent surfaces.</summary>
public sealed class OpenHarmonyRenderingOptions
{
    /// <summary>Tries EGL and Skia GPU rendering before falling back to the software framebuffer.</summary>
    public bool UseEgl { get; init; }
}
