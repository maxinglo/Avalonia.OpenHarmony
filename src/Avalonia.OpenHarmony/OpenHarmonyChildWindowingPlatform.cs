using Avalonia.Platform;

namespace Avalonia.OpenHarmony;

internal sealed class OpenHarmonyChildWindowingPlatform(
    Func<OpenHarmonyChildWindow> createWindow,
    Func<ITopLevelImpl> getEmbeddableTopLevel) : IWindowingPlatform
{
    public IWindowImpl CreateWindow() => createWindow();

    public IWindowImpl CreateEmbeddableWindow() =>
        throw new PlatformNotSupportedException("HarmonyOS cannot embed an independent Window.");

    public ITopLevelImpl CreateEmbeddableTopLevel() => getEmbeddableTopLevel();
    public ITrayIconImpl? CreateTrayIcon() => null;

    public void GetWindowsZOrder(ReadOnlySpan<IWindowImpl> windows, Span<long> zOrder)
    {
        for (var index = 0; index < windows.Length; index++)
            zOrder[index] = windows[index] is OpenHarmonyChildWindow window ? window.ZOrder : 0;
    }
}
