using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using static Avalonia.OpenHarmony.InputMethodApi;

namespace Avalonia.OpenHarmony;

internal sealed unsafe class NativeTextInput(
    SurfaceTopLevel surface,
    Func<int> getWindowId,
    Action<string> reportError) : ITextInputMethodImpl
{
    private static readonly ConcurrentDictionary<nint, Session> Sessions = new();
    private readonly Dispatcher _dispatcher = Dispatcher.UIThread;
    private readonly SurfaceTopLevel _surface = surface;
    private TextInputMethodClient? _client;
    private TextInputOptions _options = TextInputOptions.Default;
    private Session? _session;
    private Rect _cursor;

    public void SetClient(TextInputMethodClient? client)
    {
        if (ReferenceEquals(_client, client))
            return;
        if (_client is { } previous)
        {
            previous.SelectionChanged -= UpdateSelection;
            previous.SurroundingTextChanged -= UpdateSelection;
            previous.InputPaneActivationRequested -= ShowKeyboard;
            Detach();
        }
        _client = client;
        if (client is null)
            return;

        client.SelectionChanged += UpdateSelection;
        client.SurroundingTextChanged += UpdateSelection;
        client.InputPaneActivationRequested += ShowKeyboard;
        TryAttach();
    }

    public void SetOptions(TextInputOptions options) => _options = options;

    public void Reset() => _client?.SetPreeditText(null);

    public void SetCursorRect(Rect rect)
    {
        _cursor = rect;
        if (_session is { Proxy: not 0 } session)
            UpdateCursor(session.Proxy);
    }

    private void TryAttach()
    {
        try
        {
            Attach();
        }
        catch (Exception error)
        {
            reportError($"Could not attach the HarmonyOS input method. {error.Message}");
        }
    }

    private void Attach()
    {
        var windowId = getWindowId();
        if (windowId <= 0)
            return;
        var editor = InputMethodApi.OH_TextEditorProxy_Create();
        if (editor == 0)
            throw new InvalidOperationException("OH_TextEditorProxy_Create failed.");
        nint options = 0;
        try
        {
            var session = new Session(this, editor, windowId);
            CaptureSelection(session);
            Sessions[editor] = session;
            Check(InputMethodApi.OH_TextEditorProxy_SetGetTextConfigFunc(editor, &GetTextConfig));
            Check(InputMethodApi.OH_TextEditorProxy_SetInsertTextFunc(editor, &InsertText));
            Check(InputMethodApi.OH_TextEditorProxy_SetDeleteBackwardFunc(editor, &DeleteBackward));
            Check(InputMethodApi.OH_TextEditorProxy_SetDeleteForwardFunc(editor, &DeleteForward));
            Check(InputMethodApi.OH_TextEditorProxy_SetSendEnterKeyFunc(editor, &SendEnterKey));
            Check(InputMethodApi.OH_TextEditorProxy_SetHandleSetSelectionFunc(editor, &SetSelection));
            // The native attach function validates all 15 handlers, including disabled features.
            Check(InputMethodApi.OH_TextEditorProxy_SetSendKeyboardStatusFunc(editor, &KeyboardStatusChanged));
            Check(InputMethodApi.OH_TextEditorProxy_SetMoveCursorFunc(editor, &MoveCursor));
            Check(InputMethodApi.OH_TextEditorProxy_SetHandleExtendActionFunc(editor, &HandleExtendAction));
            Check(InputMethodApi.OH_TextEditorProxy_SetGetLeftTextOfCursorFunc(editor, &GetLeftText));
            Check(InputMethodApi.OH_TextEditorProxy_SetGetRightTextOfCursorFunc(editor, &GetRightText));
            Check(InputMethodApi.OH_TextEditorProxy_SetGetTextIndexAtCursorFunc(editor, &GetCaretIndex));
            Check(InputMethodApi.OH_TextEditorProxy_SetReceivePrivateCommandFunc(editor, &ReceivePrivateCommand));
            Check(InputMethodApi.OH_TextEditorProxy_SetSetPreviewTextFunc(editor, &SetPreviewText));
            Check(InputMethodApi.OH_TextEditorProxy_SetFinishTextPreviewFunc(editor, &FinishTextPreview));
            Check(InputMethodApi.OH_TextEditorProxy_SetCallbackInMainThread(editor, 0));
            options = InputMethodApi.OH_AttachOptions_Create(1);
            if (options == 0)
                throw new InvalidOperationException("OH_AttachOptions_Create failed.");
            Check(InputMethodApi.OH_InputMethodController_Attach(editor, options, out var proxy));
            session.Proxy = proxy;
            session.Options = options;
            _session = session;
            NotifySelection(session);
            UpdateCursor(proxy);
        }
        finally
        {
            if (_session is null)
            {
                if (options != 0)
                    InputMethodApi.OH_AttachOptions_Destroy(options);
                Sessions.TryRemove(editor, out _);
                InputMethodApi.OH_TextEditorProxy_Destroy(editor);
            }
        }
    }

    private void Detach()
    {
        if (_session is not { } session)
            return;
        _session = null;
        Sessions.TryRemove(session.Editor, out _);
        var result = InputMethodApi.OH_InputMethodController_Detach(session.Proxy);
        InputMethodApi.OH_AttachOptions_Destroy(session.Options);
        InputMethodApi.OH_TextEditorProxy_Destroy(session.Editor);
        if (result != 0)
            reportError($"Could not detach the HarmonyOS input method (error {result}).");
    }

    private void ShowKeyboard(object? sender, EventArgs args)
    {
        if (_session is { Proxy: not 0 } session)
            InputMethodApi.OH_InputMethodProxy_ShowKeyboard(session.Proxy);
        else
            TryAttach();
    }

    private void UpdateSelection(object? sender, EventArgs args)
    {
        if (_session is { } session)
        {
            CaptureSelection(session);
            NotifySelection(session);
        }
    }

    private void CaptureSelection(Session session)
    {
        var sendContext = _client is { SupportsSurroundingText: true } &&
            !_options.IsSensitive && _options.ContentType is not (TextInputContentType.Password or TextInputContentType.Pin);
        var text = sendContext ? _client!.SurroundingText : string.Empty;
        var range = sendContext ? _client!.Selection : default;
        var first = Math.Clamp(Math.Min(range.Start, range.End), 0, text.Length);
        var last = Math.Clamp(Math.Max(range.Start, range.End), 0, text.Length);
        var caret = Math.Clamp(range.End, 0, text.Length);
        session.Snapshot = new(text, first, last, caret, GetInputType(), GetEnterKey());
    }

    private static void NotifySelection(Session session)
    {
        var snapshot = session.Snapshot;
        fixed (char* text = snapshot.Text)
        {
            InputMethodApi.OH_InputMethodProxy_NotifySelectionChange(session.Proxy, text,
                (nuint)snapshot.Text.Length, snapshot.Start, snapshot.End);
        }
    }

    private void UpdateCursor(nint proxy)
    {
        if (_cursor.Height <= 0)
            return;
        var scale = _surface.RenderScaling;
        var cursor = InputMethodApi.OH_CursorInfo_Create(_cursor.X * scale, _cursor.Y * scale,
            _cursor.Width * scale, _cursor.Height * scale);
        if (cursor == 0)
            return;
        try
        {
            InputMethodApi.OH_InputMethodProxy_NotifyCursorUpdate(proxy, cursor);
        }
        finally
        {
            InputMethodApi.OH_CursorInfo_Destroy(cursor);
        }
    }

    private void Delete(TextInputMethodClient client, int length, bool backward)
    {
        var key = backward ? Key.Back : Key.Delete;
        var selection = client.Selection;
        if (length > 1 && client.SupportsSurroundingText && selection.Start == selection.End)
        {
            var size = client.SurroundingText.Length;
            var position = Math.Clamp(selection.Start, 0, size);
            var count = Math.Min(length, backward ? position : size - position);
            var start = backward ? position - count : position;
            client.Selection = new(start, start + count);
        }
        _surface.PressKey(key);
    }

    private EditorType GetInputType() => _options.ContentType switch
    {
        TextInputContentType.Digits => EditorType.Number,
        TextInputContentType.Pin => EditorType.NumberPassword,
        TextInputContentType.Number => EditorType.Decimal,
        TextInputContentType.Email => EditorType.Email,
        TextInputContentType.Url => EditorType.Url,
        TextInputContentType.Password => EditorType.Password,
        _ => _options.Multiline ? EditorType.Multiline : EditorType.Text
    };

    private EnterKey GetEnterKey() => _options.ReturnKeyType switch
    {
        TextInputReturnKeyType.Go => EnterKey.Go,
        TextInputReturnKeyType.Search => EnterKey.Search,
        TextInputReturnKeyType.Send => EnterKey.Send,
        TextInputReturnKeyType.Next => EnterKey.Next,
        TextInputReturnKeyType.Done => EnterKey.Done,
        TextInputReturnKeyType.Previous => EnterKey.Previous,
        TextInputReturnKeyType.Return => EnterKey.Newline,
        _ => _options.Multiline ? EnterKey.Newline : EnterKey.Unspecified
    };

    private static void Check(int status, [CallerArgumentExpression(nameof(status))] string? operation = null)
    {
        if (status != 0)
            throw new InvalidOperationException($"{operation} returned HarmonyOS input method error {status}.");
    }

    private static void Queue(nint editor, Action<NativeTextInput, TextInputMethodClient> action)
    {
        if (!Sessions.TryGetValue(editor, out var session))
            return;
        var input = session.Input;
        input._dispatcher.Post(() =>
        {
            try
            {
                if (input._session == session && input._client is { } client)
                    action(input, client);
            }
            catch (Exception error)
            {
                input.ReportInputFailure(error);
            }
        }, DispatcherPriority.Input);
    }

    private void ReportInputFailure(Exception error) =>
        reportError($"Could not process the HarmonyOS input method event. {error.GetType().Name}");

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void GetTextConfig(nint editor, nint config)
    {
        if (!Sessions.TryGetValue(editor, out var session))
            return;
        var state = session.Snapshot;
        InputMethodApi.OH_TextConfig_SetWindowId(config, session.WindowId);
        InputMethodApi.OH_TextConfig_SetInputType(config, state.InputType);
        InputMethodApi.OH_TextConfig_SetEnterKeyType(config, state.EnterKey);
        InputMethodApi.OH_TextConfig_SetSelection(config, state.Start, state.End);
        InputMethodApi.OH_TextConfig_SetPreviewTextSupport(config, 0);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void InsertText(nint editor, char* text, nuint length)
    {
        if (text != null && length is > 0 and <= 1_048_576)
        {
            var value = new string(text, 0, (int)length);
            Queue(editor, (input, _) => input._surface.InsertText(value));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DeleteBackward(nint editor, int length)
    {
        if (length > 0)
            Queue(editor, (input, client) => input.Delete(client, length, backward: true));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DeleteForward(nint editor, int length)
    {
        if (length > 0)
            Queue(editor, (input, client) => input.Delete(client, length, backward: false));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SendEnterKey(nint editor, int enterKey) =>
        Queue(editor, static (input, _) => input._surface.PressKey(Key.Enter));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SetSelection(nint editor, int start, int end) =>
        Queue(editor, (_, client) =>
        {
            if (client.SupportsSurroundingText)
            {
                var length = client.SurroundingText.Length;
                client.Selection = new(Math.Clamp(start, 0, length), Math.Clamp(end, 0, length));
            }
        });

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void KeyboardStatusChanged(nint editor, int status) { }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MoveCursor(nint editor, CursorDirection direction)
    {
        var key = direction switch
        {
            CursorDirection.Up => Key.Up,
            CursorDirection.Down => Key.Down,
            CursorDirection.Left => Key.Left,
            CursorDirection.Right => Key.Right,
            _ => Key.None
        };
        if (key != Key.None)
            Queue(editor, (input, _) => input._surface.PressKey(key));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void HandleExtendAction(nint editor, ExtendAction action)
    {
        if (action == ExtendAction.SelectAll)
            Queue(editor, static (_, client) => client.ExecuteContextMenuAction(ContextMenuAction.SelectAll));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int GetCaretIndex(nint editor) =>
        Sessions.TryGetValue(editor, out var session) ? session.Snapshot.Caret : 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void GetLeftText(nint editor, int number, char* output, nuint* length) =>
        GetSurroundingText(editor, number, output, length, left: true);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void GetRightText(nint editor, int number, char* output, nuint* length) =>
        GetSurroundingText(editor, number, output, length, left: false);

    private static void GetSurroundingText(nint editor, int number, char* output, nuint* length, bool left)
    {
        if (length == null)
            return;
        var bufferSize = *length;
        *length = 0;
        if (output == null || number <= 0 || !Sessions.TryGetValue(editor, out var session))
            return;

        var snapshot = session.Snapshot;
        var available = left ? snapshot.Caret : snapshot.Text.Length - snapshot.Caret;
        var count = Math.Min(number, available);
        if (bufferSize < (nuint)count)
            count = (int)bufferSize;
        var start = left ? snapshot.Caret - count : snapshot.Caret;
        snapshot.Text.AsSpan(start, count).CopyTo(new Span<char>(output, count));
        *length = (nuint)count;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ReceivePrivateCommand(nint editor, nint* commands, nuint size) => UnsupportedEditorOperation;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SetPreviewText(nint editor, char* text, nuint length, int start, int end) => UnsupportedEditorOperation;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FinishTextPreview(nint editor) { }

    private sealed class Session(NativeTextInput input, nint editor, int windowId)
    {
        public NativeTextInput Input { get; } = input;
        public nint Editor { get; } = editor;
        public int WindowId { get; } = windowId;
        public nint Proxy { get; set; }
        public nint Options { get; set; }
        public volatile SelectionSnapshot Snapshot =
            new(string.Empty, 0, 0, 0, EditorType.Text, EnterKey.Unspecified);
    }

    private sealed record SelectionSnapshot(string Text, int Start, int End, int Caret, EditorType InputType, EnterKey EnterKey);
}
