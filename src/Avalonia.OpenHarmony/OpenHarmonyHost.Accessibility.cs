namespace Avalonia.OpenHarmony;

public sealed partial class OpenHarmonyHost
{
    private OpenHarmonyAccessibilityTree? _accessibility;
    private long _accessibilityRegistration;

    /// <summary>Starts or stops publishing semantics for one registration of the active surface.</summary>
    public void SetAccessibility(long surface, long registration, bool enabled,
        Action<OpenHarmonyAccessibilitySnapshot>? publish = null) => Post(() =>
    {
        if (_topLevel?.Surface != surface || registration <= 0 || _root is null)
            return;
        if (!enabled)
        {
            if (_accessibility?.Registration == registration)
                CloseAccessibility();
            return;
        }
        if (_accessibilityRegistration >= registration)
            return;

        CloseAccessibility();
        _accessibilityRegistration = registration;
        _accessibility = new OpenHarmonyAccessibilityTree(_root, surface, registration,
            publish ?? OpenHarmonyAccessibilityBridge.Publish, _reportError);
    });

    /// <summary>Invokes an enabled semantic node on the Avalonia UI dispatcher.</summary>
    public void InvokeAccessibility(long surface, long registration, int id) => Post(() =>
    {
        if (_topLevel?.Surface == surface && _accessibility?.Registration == registration)
            _accessibility.Invoke(id);
    });

    private void CloseAccessibility()
    {
        _accessibility?.Dispose();
        _accessibility = null;
    }
}
