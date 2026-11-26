using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Controls.Platform;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Avalonia.OpenHarmony;

/// <summary>Opts into Avalonia tray icons on HarmonyOS PCs with a status bar.</summary>
public sealed class OpenHarmonyTrayOptions
{
    public Func<string, string, Task<OpenHarmonySystemResult>> Request { get; init; }
        = OpenHarmonySystemRequests.RequestAsync;

    public async Task<bool> IsSupportedAsync()
    {
        var result = await Request("tray.supported", string.Empty).ConfigureAwait(false);
        result.File?.Dispose();
        return result.Value == "true";
    }
}

internal sealed class OpenHarmonyTrayIcon : ITrayIconImpl
{
    private static long s_nextId;
    private readonly OpenHarmonyTrayOptions _options;
    private readonly Action<string> _reportError;
    private readonly Action _released;
    private readonly OpenHarmonyNativeMenuExporter _menu;
    private string? _icon;
    private string? _toolTip;
    private string _menuJson = "[]";
    private bool _visible;
    private bool _mightBeShown;
    private bool _dirty;
    private bool _scheduled;
    private bool _sending;
    private bool _disposed;

    public OpenHarmonyTrayIcon(OpenHarmonyTrayOptions options, Action<string> reportError, Action released)
    {
        _options = options;
        _reportError = reportError;
        _released = released;
        _menu = new OpenHarmonyNativeMenuExporter(json =>
        {
            if (_menuJson != json)
            {
                _menuJson = json;
                Invalidate();
            }
        });
    }

    public long Id { get; } = Interlocked.Increment(ref s_nextId);
    public INativeMenuExporter MenuExporter => _menu;
    public Action? OnClicked { get; set; }

    public void SetIcon(IWindowIconImpl? icon)
    {
        if (_disposed)
            return;
        string? png = null;
        if (icon is not null)
        {
            using var buffer = new MemoryStream();
            icon.Save(buffer);
            png = Convert.ToBase64String(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
        }
        if (_icon == png)
            return;
        _icon = png;
        Invalidate();
    }

    public void SetToolTipText(string? text)
    {
        if (text is { Length: > 128 })
            text = text[..(char.IsHighSurrogate(text[127]) ? 127 : 128)];
        if (_toolTip == text)
            return;
        _toolTip = text;
        Invalidate();
    }

    public void SetIsVisible(bool visible)
    {
        if (_visible == visible)
            return;
        _visible = visible;
        Invalidate();
    }

    public void ReportEvent(string menuCode)
    {
        if (_disposed || !_visible)
            return;
        if (menuCode.Length == 0)
            OnClicked?.Invoke();
        else
            _menu.Invoke(menuCode);
    }

    public void Invalidate()
    {
        if (_disposed)
            return;
        _dirty = true;
        if (_scheduled || _sending)
            return;
        _scheduled = true;
        Dispatcher.UIThread.Post(PublishChanges, DispatcherPriority.Background);
    }

    private async void PublishChanges()
    {
        _scheduled = false;
        _dirty = false;
        if (_disposed || (!_visible && !_mightBeShown))
            return;
        _sending = true;
        var visible = _visible;
        if (visible)
            _mightBeShown = true;
        if (await SendAsync(visible) && !visible)
            _mightBeShown = false;
        _sending = false;
        if (_dirty)
            Invalidate();
    }

    private async Task<bool> SendAsync(bool visible)
    {
        try
        {
            using var buffer = new MemoryStream();
            using (var json = new Utf8JsonWriter(buffer))
            {
                json.WriteStartObject();
                json.WriteString("id", Id.ToString(CultureInfo.InvariantCulture));
                json.WriteBoolean("visible", visible);
                if (visible)
                {
                    json.WriteString("icon", _icon);
                    json.WriteString("toolTip", _toolTip);
                    json.WriteString("menu", _menuJson);
                }
                json.WriteEndObject();
            }
            var data = Encoding.UTF8.GetString(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
            var result = await _options.Request("tray.apply", data).ConfigureAwait(false);
            result.File?.Dispose();
            if (result.Value is not null)
                return true;
            _reportError(visible ? "OpenHarmony could not apply the status bar item."
                : "OpenHarmony could not remove the status bar item.");
        }
        catch (Exception error)
        {
            _reportError($"OpenHarmony status bar: {error.Message}");
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_mightBeShown)
            _ = SendAsync(false);
        OnClicked = null;
        _menu.Dispose();
        _released();
    }
}
