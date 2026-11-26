using Avalonia.Input;

namespace Avalonia.OpenHarmony;

public sealed partial class OpenHarmonyHost
{
    /// <summary>Reports the title bar height and the native button reservation in DIPs.</summary>
    public void ReportTitleBar(bool extended, double height, double buttonInset)
    {
        if (double.IsFinite(height) && double.IsFinite(buttonInset) && height >= 0 && buttonInset >= 0)
            Post(() => _stageRoute?.Window.ReportTitleBar(extended, height, buttonInset));
    }

    /// <summary>Dispatches a press while ArkUI can still begin a native window move.</summary>
    public bool DispatchTitleBarPress(long surface, bool mouse, long contact, double x, double y, int modifiers)
    {
        bool DispatchPress(Func<bool> acceptMove) =>
            _stageRoute is { } stage && stage.Token == surface && stage.Window.AcceptsInput &&
            stage.Window.DispatchTitleBarPress(mouse, contact, x, y, (RawInputModifiers)modifiers, acceptMove);

        TaskCompletionSource<bool>? result = null;
        lock (_dispatchLock)
        {
            if (_stopped || _dispatcher is not { } dispatcher)
                return false;
            if (!dispatcher.CheckAccess())
            {
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                result = completion;
                Dispatch(dispatcher, () =>
                {
                    try { DispatchPress(() => completion.TrySetResult(true)); }
                    finally { completion.TrySetResult(false); }
                });
            }
        }

        if (result is null)
            return DispatchPress(static () => true);

        // Keep late presses in Avalonia without cancelling capture after the ArkUI callback returns.
        if (!result.Task.Wait(TimeSpan.FromMilliseconds(40)))
            result.TrySetResult(false);
        return result.Task.Result;
    }
}
