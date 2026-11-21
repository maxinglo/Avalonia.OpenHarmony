using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using static Avalonia.OpenHarmony.InputMethodApi;

namespace Avalonia.OpenHarmony;

internal sealed unsafe partial class NativeTextInput : ITextInputMethodImpl, IDisposable
{
    private const int InvalidPreviewRange = 401;
    private const int PreviewNotSupported = 12800011;
    private const int MaximumTextLength = 1_048_576;
    private static readonly ConcurrentDictionary<nint, Session> Sessions = new();
    private readonly Dispatcher _dispatcher = Dispatcher.UIThread;
    private readonly SurfaceTopLevel _surface;
    private readonly Func<int> _getWindowId;
    private readonly Action<string> _reportError;
    private readonly OpenHarmonyInputDiagnostics? _diagnostics =
        AvaloniaLocator.Current.GetService<OpenHarmonyInputDiagnostics>();
    private TextInputMethodClient? _client;
    private TextInputOptions _options = TextInputOptions.Default;
    private Session? _session;
    private PreviewComposition? _preview;
    private PreviewDisplay? _previewDisplay;
    private bool _updatingPreview;
    private Rect _cursor;

    public NativeTextInput(SurfaceTopLevel surface, Func<int> getWindowId, Action<string> reportError)
    {
        _surface = surface;
        _getWindowId = getWindowId;
        _reportError = reportError;
    }

    public void SetClient(TextInputMethodClient? client)
    {
        if (_disposed || ReferenceEquals(_client, client))
            return;
        CancelAttachRequest();
        if (_client is { } previous)
        {
            previous.SelectionChanged -= UpdateSelection;
            previous.SurroundingTextChanged -= UpdateSelection;
            previous.TextViewVisualChanged -= UpdateSelection;
            previous.InputPaneActivationRequested -= ShowKeyboard;
            ClearPreview(previous);
            Detach();
        }
        _client = client;
        if (client is null)
            return;

        client.SelectionChanged += UpdateSelection;
        client.SurroundingTextChanged += UpdateSelection;
        client.TextViewVisualChanged += UpdateSelection;
        client.InputPaneActivationRequested += ShowKeyboard;
        RequestAttach();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        SetClient(null);
        _disposed = true;
    }

    public void SetOptions(TextInputOptions options)
    {
        _options = options;
        if (_client is { } client && IsSensitive(client))
        {
            ClearPreview(client);
            if (_session is { } session)
                CaptureSelection(session);
        }
    }

    public void Reset()
    {
        if (_client is not { } client)
            return;
        if (ClearPreview(client) is not null)
            RefreshSelection();
        else
            client.SetPreeditText(null);
    }

    public void SetCursorRect(Rect rect)
    {
        _cursor = rect;
        if (!_updatingPreview && _session is { Proxy: not 0 } session)
            UpdateCursor(session.Proxy);
    }

    private int Attach(int windowId)
    {
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
            var result = InputMethodApi.OH_InputMethodController_Attach(editor, options, out var proxy);
            if (result != 0)
                return result;
            if (proxy == 0)
                throw new InvalidOperationException("OH_InputMethodController_Attach returned an empty proxy.");
            session.Proxy = proxy;
            session.Options = options;
            _session = session;
            session.Active = true;
            _diagnostics?.Report($"IME attached: window {windowId}");
            NotifySelection(session);
            UpdateCursor(proxy);
            return result;
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
        session.Active = false;
        Sessions.TryRemove(session.Editor, out _);
        var result = InputMethodApi.OH_InputMethodController_Detach(session.Proxy);
        InputMethodApi.OH_AttachOptions_Destroy(session.Options);
        InputMethodApi.OH_TextEditorProxy_Destroy(session.Editor);
        if (result != 0)
            _reportError($"Could not detach the HarmonyOS input method (error {result}).");
    }

    private void ShowKeyboard(object? sender, EventArgs args)
    {
        if (!_inputMethodActive || _disposed)
            return;
        if (_session is { Proxy: not 0 } session)
        {
            var result = InputMethodApi.OH_InputMethodProxy_ShowKeyboard(session.Proxy);
            if (result != 0)
                _reportError($"OH_InputMethodProxy_ShowKeyboard returned HarmonyOS input method error {result}.");
            return;
        }
        RequestAttach();
    }

    private void UpdateSelection(object? sender, EventArgs args)
    {
        if (_updatingPreview)
            return;
        if (_client is { } client)
            ClearPreview(client);
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        if (_session is { } session)
        {
            CaptureSelection(session);
            NotifySelection(session);
        }
    }

    private void CaptureSelection(Session session)
    {
        var client = _client;
        var sensitive = client is null || IsSensitive(client);
        var sendContext = client is { SupportsSurroundingText: true } && !sensitive;
        var text = sendContext ? client!.SurroundingText : string.Empty;
        var range = sendContext ? client!.Selection : default;
        var first = Math.Clamp(Math.Min(range.Start, range.End), 0, text.Length);
        var last = Math.Clamp(Math.Max(range.Start, range.End), 0, text.Length);
        var caret = Math.Clamp(range.End, 0, text.Length);
        var canPreview = sendContext && client is { SupportsPreedit: true, TextViewVisual: TextPresenter };
        session.Snapshot = new(text, first, last, caret, GetInputType(), GetEnterKey(), canPreview, sensitive);
    }

    private bool IsSensitive(TextInputMethodClient client) =>
        _options.IsSensitive || _options.ContentType is TextInputContentType.Password or TextInputContentType.Pin ||
        client.TextViewVisual is TextPresenter { PasswordChar: not '\0' };

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
        if (_preview is { } preview && _session is { } session)
        {
            // Within a composition, deletion edits the provisional text. It does not touch the backing TextBox.
            var caret = session.Snapshot.Caret;
            var count = Math.Min(length, backward ? caret - preview.Start : preview.End - caret);
            var first = backward ? caret - count : caret;
            var last = first + count;
            if (!IsCharacterBoundary(preview.Text, first - preview.Start))
                first--;
            if (!IsCharacterBoundary(preview.Text, last - preview.Start))
                last++;
            ApplyPreview(session, client, preview.Replace(first, last, string.Empty), first);
            return;
        }
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
        _reportError($"Could not process the HarmonyOS input method event. {error.GetType().Name}");

    private int UpdatePreview(Session session, string value, int start, int end)
    {
        var client = _client;
        if (_session != session || client is null)
            return UnsupportedEditorOperation;
        if (!session.Snapshot.SupportsPreview || IsSensitive(client) || client.TextViewVisual is not TextPresenter presenter)
            return PreviewNotSupported;

        var defaultRange = start == -1 && end == -1;
        if (_preview is { } current)
        {
            // An explicit range uses absolute UTF-16 offsets in the currently displayed composition.
            if (defaultRange)
                (start, end) = (current.Start, current.End);
            if (start < current.Start || end < start || end > current.End)
                return InvalidPreviewRange;
            if (!IsCharacterBoundary(current.Text, start - current.Start) ||
                !IsCharacterBoundary(current.Text, end - current.Start) ||
                (long)current.Text.Length - (end - start) + value.Length > MaximumTextLength)
                return InvalidPreviewRange;
            ApplyPreview(session, client, current.Replace(start, end, value), start + value.Length);
        }
        else
        {
            var selection = session.Snapshot;
            var text = selection.Text;
            if (defaultRange)
                (start, end) = (selection.Start, selection.End);
            else if (selection.Start != selection.End)
                return InvalidPreviewRange;
            if (start < 0 || end < start || end > text.Length || !IsCharacterBoundary(text, start) ||
                !IsCharacterBoundary(text, end))
                return InvalidPreviewRange;
            ApplyPreview(session, client, new(presenter, text.Remove(start, end - start), start, end, value),
                start + value.Length);
        }
        return 0;
    }

    private static bool IsCharacterBoundary(string text, int index) =>
        index == 0 || index == text.Length ||
        !(char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index]));

    private void ApplyPreview(Session session, TextInputMethodClient client, PreviewComposition preview, int caret)
    {
        _updatingPreview = true;
        try
        {
            // The Avalonia TextPresenter inserts preedit at its caret. Present the backing text without the
            // provisional replacement range, while preserving the owning TextBox's committed value and selection.
            _previewDisplay ??= new PreviewDisplay(preview);
            _preview = preview;
            client.SetPreeditText(preview.Text, caret - preview.Start);
            var snapshot = session.Snapshot with
            {
                Text = preview.Body.Insert(preview.Start, preview.Text),
                Start = caret,
                End = caret,
                Caret = caret
            };
            session.Snapshot = snapshot;
            // Avoid synchronous callbacks into the IME while its SetPreviewText is waiting for the UI thread.
            _dispatcher.Post(() =>
            {
                if (_session == session && ReferenceEquals(session.Snapshot, snapshot))
                {
                    NotifySelection(session);
                    UpdateCursor(session.Proxy);
                }
            }, DispatcherPriority.Input);
        }
        finally
        {
            _updatingPreview = false;
        }
    }

    private PreviewComposition? ClearPreview(TextInputMethodClient client)
    {
        var preview = _preview;
        if (preview is null)
            return null;
        _preview = null;
        _updatingPreview = true;
        try
        {
            client.SetPreeditText(null);
            var display = _previewDisplay;
            _previewDisplay = null;
            display?.Dispose();
        }
        finally
        {
            _updatingPreview = false;
        }
        return preview;
    }

    private void Commit(TextInputMethodClient client, string? insertedText = null)
    {
        var preview = ClearPreview(client);
        var value = insertedText ?? preview?.Text;
        if (preview is not null)
            client.Selection = new(preview.Start, preview.ReplacedEnd);
        if (!string.IsNullOrEmpty(value))
        {
            if (_diagnostics is { } diagnostics && !IsSensitive(client))
                diagnostics.Report($"IME commit: {value.Length} UTF-16 units");
            _surface.InsertText(value);
        }
        else if (preview is not null && preview.Start != preview.ReplacedEnd)
            _surface.PressKey(Key.Delete);
        if (preview is not null)
            RefreshSelection();
    }

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
        var result = InputMethodApi.OH_TextConfig_SetPreviewTextSupport(config, state.SupportsPreview ? (byte)1 : (byte)0);
        if (!state.IsSensitive && session.Input._diagnostics is { TraceImePreview: true } diagnostics)
            diagnostics.Report($"IME preview config: supported={state.SupportsPreview} result={result}");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void InsertText(nint editor, char* text, nuint length)
    {
        if (text != null && length is > 0 and <= MaximumTextLength)
        {
            var value = new string(text, 0, (int)length);
            Queue(editor, (input, client) => input.Commit(client, value));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DeleteBackward(nint editor, int length)
    {
        if (length > 0)
            Queue(editor, (input, client) =>
            {
                if (input._diagnostics is { } diagnostics && !input.IsSensitive(client))
                    diagnostics.Report($"IME backspace: {length} UTF-16 units");
                input.Delete(client, length, backward: true);
            });
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DeleteForward(nint editor, int length)
    {
        if (length > 0)
            Queue(editor, (input, client) =>
            {
                if (input._diagnostics is { } diagnostics && !input.IsSensitive(client))
                    diagnostics.Report($"IME forward delete: {length} UTF-16 units");
                input.Delete(client, length, backward: false);
            });
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SendEnterKey(nint editor, int enterKey) =>
        Queue(editor, static (input, client) =>
        {
            input.Commit(client);
            input._surface.PressKey(Key.Enter);
        });

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SetSelection(nint editor, int start, int end) =>
        Queue(editor, (input, client) =>
        {
            if (client.SupportsSurroundingText)
            {
                input.Commit(client);
                var length = client.SurroundingText.Length;
                client.Selection = new(Math.Clamp(start, 0, length), Math.Clamp(end, 0, length));
            }
        });

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void KeyboardStatusChanged(nint editor, int status) =>
        Queue(editor, (input, _) => input._diagnostics?.Report($"IME keyboard status: {status}"));

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
            Queue(editor, (input, client) =>
            {
                input.Commit(client);
                input._surface.PressKey(key);
            });
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void HandleExtendAction(nint editor, ExtendAction action)
    {
        if (action == ExtendAction.SelectAll)
            Queue(editor, static (input, client) =>
            {
                input.Commit(client);
                client.ExecuteContextMenuAction(ContextMenuAction.SelectAll);
            });
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
    private static int SetPreviewText(nint editor, char* text, nuint length, int start, int end)
    {
        Sessions.TryGetValue(editor, out var session);
        var state = session?.Snapshot;
        var result = ProcessPreviewText(session, text, length, start, end);
        if (state is { IsSensitive: false } && session is not null && !session.Snapshot.IsSensitive &&
            session.Input._diagnostics is { TraceImePreview: true } diagnostics)
            diagnostics.Report($"IME preview set: length={length} start={start} end={end} supported={state.SupportsPreview} result={result}");
        return result;
    }

    private static int ProcessPreviewText(Session? session, char* text, nuint length, int start, int end)
    {
        if (length > MaximumTextLength || (text == null && length != 0))
            return InvalidPreviewRange;
        if (session is null || !session.Active)
            return UnsupportedEditorOperation;
        if (!session.Snapshot.SupportsPreview)
            return PreviewNotSupported;

        var input = session.Input;
        try
        {
            var value = length == 0 ? string.Empty : new string(text, 0, (int)length);
            // As in ArkUI, validate the range against the focused UI client before returning to the IME.
            return input._dispatcher.Invoke(() => input.UpdatePreview(session, value, start, end),
                DispatcherPriority.Input, CancellationToken.None, TimeSpan.FromSeconds(1));
        }
        catch (Exception error)
        {
            input.ReportInputFailure(error);
            return UnsupportedEditorOperation;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FinishTextPreview(nint editor) =>
        Queue(editor, static (input, client) =>
        {
            if (input._diagnostics is { TraceImePreview: true } diagnostics && !input.IsSensitive(client))
            {
                var preview = input._preview;
                diagnostics.Report($"IME preview finish: length={preview?.Text.Length ?? 0} start={preview?.Start ?? -1} end={preview?.End ?? -1}");
            }
            input.Commit(client);
        });

    private sealed class Session(NativeTextInput input, nint editor, int windowId)
    {
        public NativeTextInput Input { get; } = input;
        public nint Editor { get; } = editor;
        public int WindowId { get; } = windowId;
        public nint Proxy { get; set; }
        public nint Options { get; set; }
        public volatile bool Active;
        public volatile SelectionSnapshot Snapshot =
            new(string.Empty, 0, 0, 0, EditorType.Text, EnterKey.Unspecified, false, true);
    }

    private sealed record PreviewComposition(TextPresenter Presenter, string Body, int Start, int ReplacedEnd, string Text)
    {
        public int End => Start + Text.Length;

        public PreviewComposition Replace(int start, int end, string value) =>
            this with { Text = Text.Remove(start - Start, end - start).Insert(start - Start, value) };
    }

    private sealed class PreviewDisplay : IDisposable
    {
        private readonly IDisposable? _text;
        private readonly IDisposable? _caret;
        private readonly IDisposable? _selectionStart;
        private readonly IDisposable? _selectionEnd;

        public PreviewDisplay(PreviewComposition preview)
        {
            var presenter = preview.Presenter;
            try
            {
                _text = Override(presenter, TextPresenter.TextProperty, preview.Body);
                _caret = Override(presenter, TextPresenter.CaretIndexProperty, preview.Start);
                _selectionStart = Override(presenter, TextPresenter.SelectionStartProperty, preview.Start);
                _selectionEnd = Override(presenter, TextPresenter.SelectionEndProperty, preview.Start);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _text?.Dispose();
            _caret?.Dispose();
            _selectionStart?.Dispose();
            _selectionEnd?.Dispose();
        }

        private static IDisposable? Override<T>(TextPresenter presenter, StyledProperty<T> property, T value)
        {
            if (EqualityComparer<T>.Default.Equals(presenter.GetValue(property), value))
                return null;
            // A stock TextBox uses a two-way template binding for TextPresenter.Text. Pause the binding
            // for the synchronous write, then resume it under the temporary display-only value.
            if (BindingOperations.GetBindingExpressionBase(presenter, property) is not
                UntypedBindingExpressionBase { IsRunning: true } binding)
                return presenter.SetValue(property, value, BindingPriority.Animation);
            StopBinding(binding);
            try
            {
                var valueHandle = presenter.SetValue(property, value, BindingPriority.Animation);
                return valueHandle is not null ? new DisplayOverride(valueHandle, binding) : null;
            }
            finally
            {
                binding.Start();
            }
        }

        // The backend pins Avalonia 12.1.6. UnsafeAccessor lets NativeAOT resolve the matching member.
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Stop")]
        private static extern void StopBinding(UntypedBindingExpressionBase binding);

        private sealed class DisplayOverride(IDisposable value, UntypedBindingExpressionBase binding) : IDisposable
        {
            public void Dispose()
            {
                // A source-change event can reach the editor before the template binding observes it.
                // Refresh the current source while the override still hides the old display value.
                if (binding.IsRunning)
                {
                    StopBinding(binding);
                    binding.Start();
                }
                value.Dispose();
            }
        }
    }

    private sealed record SelectionSnapshot(string Text, int Start, int End, int Caret, EditorType InputType, EnterKey EnterKey,
        bool SupportsPreview, bool IsSensitive);
}
