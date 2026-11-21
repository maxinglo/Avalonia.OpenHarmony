using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace Avalonia.OpenHarmony;

/// <summary>Uses the on-device HarmonyOS drag service for Avalonia plain-text drags.</summary>
public sealed class OpenHarmonyTextDragOptions
{
    public Func<string, string, Task<OpenHarmonySystemResult>> Request { get; init; }
        = OpenHarmonySystemRequests.RequestAsync;
}

internal sealed class OpenHarmonyTextDragGesture(long token)
{
    internal long Token { get; } = token;
}

internal sealed class OpenHarmonyTextDragSource(
    Func<string, string, Task<OpenHarmonySystemResult>> request, Action<string> reportError) : IPlatformDragSource
{
    private const int MaximumTextBytes = 64 * 1024;

    public async Task<DragDropEffects> DoDragDropAsync(PointerPressedEventArgs triggerEvent, IDataTransfer transfer,
        DragDropEffects allowedEffects)
    {
        using (transfer)
        {
            Dispatcher.UIThread.VerifyAccess();
            if ((allowedEffects & DragDropEffects.Copy) == 0 ||
                triggerEvent.Pointer.Type != PointerType.Touch ||
                triggerEvent.PlatformInputEventCookie is not OpenHarmonyTextDragGesture gesture ||
                transfer.TryGetText() is not { } text ||
                Encoding.UTF8.GetByteCount(text) > MaximumTextBytes)
                return DragDropEffects.None;

            var token = gesture.Token.ToString(CultureInfo.InvariantCulture);
            var argument = $"{{\"gesture\":{token},\"text\":\"{JsonEncodedText.Encode(text)}\"}}";
            triggerEvent.Pointer.Capture(null);
            try
            {
                var result = await request("drag.text", argument);
                result.File?.Dispose();
                return result.Value == "copy" ? DragDropEffects.Copy : DragDropEffects.None;
            }
            catch (Exception error)
            {
                reportError($"HarmonyOS text drag failed: {error.Message}");
                return DragDropEffects.None;
            }
        }
    }
}
