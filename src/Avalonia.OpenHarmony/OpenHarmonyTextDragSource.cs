using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace Avalonia.OpenHarmony;

/// <summary>Uses the on-device HarmonyOS drag service for Avalonia text, HTML, and web links.</summary>
public sealed class OpenHarmonyTextDragOptions
{
    public Func<string, string, Task<OpenHarmonySystemResult>> Request { get; init; }
        = OpenHarmonySystemRequests.RequestAsync;
}

internal sealed class OpenHarmonyTextDragGesture(long token)
{
    internal long Token { get; } = token;
}

internal sealed class OpenHarmonyTextDragSource(OpenHarmonyTextDragOptions options, Action<string> reportError)
    : IPlatformDragSource
{
    private const int MaximumTextBytes = 64 * 1024;
    private const int MaximumTextItems = 32;

    public async Task<DragDropEffects> DoDragDropAsync(PointerPressedEventArgs triggerEvent, IDataTransfer transfer,
        DragDropEffects allowedEffects)
    {
        using (transfer)
        {
            Dispatcher.UIThread.VerifyAccess();
            var effects = allowedEffects & (DragDropEffects.Copy | DragDropEffects.Move);
            if (effects == DragDropEffects.None || triggerEvent.Pointer.Type is not (PointerType.Touch or PointerType.Mouse) ||
                triggerEvent.PlatformInputEventCookie is not OpenHarmonyTextDragGesture gesture)
                return DragDropEffects.None;

            // Reject the entire offer when it includes files because ArkUI can grant write access even with a read-only policy.
            if (transfer.Formats.Contains(DataFormat.File) ||
                transfer.Items.Any(item => item.Formats.Contains(DataFormat.File)))
                return DragDropEffects.None;

            try
            {
                var items = ReadItems(transfer);
                if (items is not { Count: > 0 })
                    return DragDropEffects.None;

                var (operation, argument) = Encode(gesture.Token, items, effects);
                triggerEvent.Pointer.Capture(null);
                var result = await options.Request(operation, argument);
                result.File?.Dispose();
                return result.Value switch
                {
                    "copy" => effects & DragDropEffects.Copy,
                    "move" => effects & DragDropEffects.Move,
                    _ => DragDropEffects.None
                };
            }
            catch (Exception error)
            {
                reportError($"HarmonyOS drag failed: {error.Message}");
                return DragDropEffects.None;
            }
        }
    }

    private static List<DragItem>? ReadItems(IDataTransfer transfer)
    {
        var items = new List<DragItem>();
        var remainingBytes = MaximumTextBytes;
        foreach (var item in transfer.Items)
        {
            var text = item.Formats.Contains(DataFormat.Text) ? item.TryGetText() : null;
            var html = item.Formats.Contains(OpenHarmonyDragFormats.Html) ? item.TryGetValue(OpenHarmonyDragFormats.Html) : null;
            var link = item.Formats.Contains(OpenHarmonyDragFormats.UriList) ? item.TryGetValue(OpenHarmonyDragFormats.UriList) : null;
            if (link is not null && !OpenHarmonyDragFormats.IsWebLink(link))
                link = null;
            if (text is null && html is null && link is null)
                continue;
            remainingBytes -= Bytes(text) + Bytes(html) + Bytes(link);
            if (items.Count == MaximumTextItems || remainingBytes < 0)
                return null;
            items.Add(new DragItem(text, html, link));
        }
        return items;
    }

    private static int Bytes(string? value) => value is null ? 0 : Encoding.UTF8.GetByteCount(value);

    private static (string Operation, string Argument) Encode(long token, List<DragItem> items, DragDropEffects effects)
    {
        if (effects == DragDropEffects.Copy && items.All(item => item is { Html: null, Link: null }))
        {
            var gesture = token.ToString(CultureInfo.InvariantCulture);
            var texts = items.Select(item => $"\"{JsonEncodedText.Encode(item.Text!)}\"");
            return items.Count == 1
                ? ("drag.text", $"{{\"gesture\":{gesture},\"text\":{texts.Single()}}}")
                : ("drag.texts", $"{{\"gesture\":{gesture},\"texts\":[{string.Join(",", texts)}]}}");
        }

        var bytes = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(bytes))
        {
            json.WriteStartObject();
            json.WriteNumber("gesture", token);
            json.WriteNumber("effects", (int)effects);
            json.WriteStartArray("items");
            foreach (var item in items)
            {
                json.WriteStartObject();
                if (item.Text is not null)
                    json.WriteString("text", item.Text);
                if (item.Html is not null)
                    json.WriteString("html", item.Html);
                if (item.Link is not null)
                    json.WriteString("link", item.Link);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return ("drag.data", Encoding.UTF8.GetString(bytes.WrittenSpan));
    }

    private readonly record struct DragItem(string? Text, string? Html, string? Link);
}

internal static class OpenHarmonyDragFormats
{
    internal static readonly DataFormat<string> Html = DataFormat.CreateStringPlatformFormat("text/html");
    internal static readonly DataFormat<string> UriList = DataFormat.CreateStringPlatformFormat("text/uri-list");

    internal static bool IsWebLink(string value) => !value.Any(char.IsControl) &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";
}
