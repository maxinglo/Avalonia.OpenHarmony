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
    private const int MaximumTextItems = 32;

    public async Task<DragDropEffects> DoDragDropAsync(PointerPressedEventArgs triggerEvent, IDataTransfer transfer,
        DragDropEffects allowedEffects)
    {
        using (transfer)
        {
            Dispatcher.UIThread.VerifyAccess();
            if ((allowedEffects & DragDropEffects.Copy) == 0 ||
                triggerEvent.Pointer.Type != PointerType.Touch ||
                triggerEvent.PlatformInputEventCookie is not OpenHarmonyTextDragGesture gesture ||
                transfer.Formats.Contains(DataFormat.File) ||
                transfer.Items.Any(item => item.Formats.Contains(DataFormat.File)))
                return DragDropEffects.None;

            var texts = new List<string>();
            var remainingBytes = MaximumTextBytes;
            foreach (var item in transfer.Items)
            {
                if (!item.Formats.Contains(DataFormat.Text) || item.TryGetText() is not { } text)
                    continue;
                remainingBytes -= Encoding.UTF8.GetByteCount(text);
                if (texts.Count == MaximumTextItems || remainingBytes < 0)
                    return DragDropEffects.None;
                texts.Add($"\"{JsonEncodedText.Encode(text)}\"");
            }
            if (texts.Count == 0)
                return DragDropEffects.None;

            var token = gesture.Token.ToString(CultureInfo.InvariantCulture);
            var multiple = texts.Count > 1;
            var argument = multiple
                ? $"{{\"gesture\":{token},\"texts\":[{string.Join(",", texts)}]}}"
                : $"{{\"gesture\":{token},\"text\":{texts[0]}}}";
            triggerEvent.Pointer.Capture(null);
            try
            {
                var result = await request(multiple ? "drag.texts" : "drag.text", argument);
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
