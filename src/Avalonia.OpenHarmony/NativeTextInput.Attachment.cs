using Avalonia.Input.TextInput;
using Avalonia.Threading;

namespace Avalonia.OpenHarmony;

internal sealed partial class NativeTextInput
{
    // IME_ERR_IMMS from inputmethod_types_capi.h.
    private const int InputMethodServiceError = 12800008;
    private static readonly int[] AttachRetryDelaysMilliseconds = [120, 300];
    private bool _inputMethodActive = true;
    private bool _disposed;
    private AttachRequest? _attachRequest;

    public void SetInputMethodActive(bool active)
    {
        if (_disposed || _inputMethodActive == active)
            return;
        _inputMethodActive = active;
        if (!active)
        {
            CancelAttachRequest();
            Detach();
        }
    }

    private void RequestAttach()
    {
        if (!_inputMethodActive || _disposed || _session is not null || _client is not { } client)
            return;

        try
        {
            var windowId = _getWindowId();
            if (windowId <= 0)
                return;
            // Keep the request after a terminal result so a second activation from the
            // same focus change does not restart the retry budget.
            if (_attachRequest is { } current && ReferenceEquals(current.Client, client) &&
                current.WindowId == windowId)
                return;

            CancelAttachRequest();
            var request = new AttachRequest(client, windowId);
            _attachRequest = request;
            _dispatcher.Post(() => RunAttachAttempt(request), DispatcherPriority.Input);
        }
        catch (Exception error)
        {
            _reportError($"Could not prepare the HarmonyOS input method attachment. {error.Message}");
        }
    }

    private void RunAttachAttempt(AttachRequest request)
    {
        if (!ReferenceEquals(_attachRequest, request))
            return;
        request.PendingRetry = null;

        try
        {
            if (!_inputMethodActive || _disposed || !ReferenceEquals(_client, request.Client) ||
                _getWindowId() != request.WindowId)
            {
                CancelAttachRequest();
                return;
            }

            var attempt = ++request.Attempts;
            var result = Attach(request.WindowId);
            if (result == 0)
                return;
            if (result == InputMethodServiceError && attempt <= AttachRetryDelaysMilliseconds.Length)
            {
                var delay = AttachRetryDelaysMilliseconds[attempt - 1];
                _reportError($"OH_InputMethodController_Attach attempt {attempt} returned HarmonyOS input method error {result}. Retrying in {delay} ms.");
                request.PendingRetry = DispatcherTimer.RunOnce(() => RunAttachAttempt(request),
                    TimeSpan.FromMilliseconds(delay), DispatcherPriority.Input);
                return;
            }

            var outcome = result == InputMethodServiceError ? "retry budget exhausted" : "no further attempts";
            _reportError($"OH_InputMethodController_Attach failed after {attempt} attempt(s) with HarmonyOS input method error {result}. {outcome}.");
        }
        catch (Exception error)
        {
            _reportError($"Could not attach the HarmonyOS input method. {error.Message}");
        }
    }

    private void CancelAttachRequest()
    {
        var request = _attachRequest;
        _attachRequest = null;
        request?.PendingRetry?.Dispose();
    }

    private sealed class AttachRequest(TextInputMethodClient client, int windowId)
    {
        public TextInputMethodClient Client { get; } = client;
        public int WindowId { get; } = windowId;
        public int Attempts { get; set; }
        public IDisposable? PendingRetry { get; set; }
    }
}
