using Avalonia.Platform;

namespace Avalonia.OpenHarmony;

public sealed partial class OpenHarmonyHost
{
    private OpenHarmonyTrayIcon? _trayIcon;

    private ITrayIconImpl? CreateTrayIcon()
    {
        if (_trayIcon is not null || AvaloniaLocator.Current.GetService<OpenHarmonyTrayOptions>() is not { } options)
            return null;
        _trayIcon = new OpenHarmonyTrayIcon(options, _reportError, () => _trayIcon = null);
        return _trayIcon;
    }

    /// <summary>Routes a status bar icon activation or a native menu selection to Avalonia.</summary>
    public void ReportTrayEvent(long id, string menuCode) => Post(() =>
    {
        if (id == 0)
            _trayIcon?.Invalidate();
        else if (_trayIcon is { } tray && tray.Id == id)
            tray.ReportEvent(menuCode);
    });
}
