namespace Avalonia.OpenHarmony;

public sealed partial class OpenHarmonyHost
{
    private readonly Dictionary<long, AccessibilityRegistration> _accessibility = new();

    /// <summary>Starts or stops publishing semantics for one registration of a hosted surface.</summary>
    public void SetAccessibility(long surface, long registration, bool enabled,
        Action<OpenHarmonyAccessibilitySnapshot>? publish = null) => Post(() =>
    {
        if (registration <= 0)
            return;
        _accessibility.TryGetValue(surface, out var previous);
        if (!enabled)
        {
            if (previous.Generation == registration)
            {
                previous.Tree?.Dispose();
                _accessibility[surface] = new(registration, null);
            }
            return;
        }
        if (previous.Generation >= registration || FindHostedSurface(surface)?.ControlRoot is not { } root)
            return;

        previous.Tree?.Dispose();
        var tree = new OpenHarmonyAccessibilityTree(root, surface, registration,
            publish ?? OpenHarmonyAccessibilityBridge.Publish, _reportError);
        _accessibility[surface] = new(registration, tree);
    });

    /// <summary>Invokes an enabled semantic node on the Avalonia UI dispatcher.</summary>
    public void InvokeAccessibility(long surface, long registration, int id) => Post(() =>
    {
        if (FindHostedSurface(surface) is not null && _accessibility.TryGetValue(surface, out var state) &&
            state.Generation == registration)
            state.Tree?.Invoke(id);
    });

    private void CloseAccessibility(long surface)
    {
        if (_accessibility.Remove(surface, out var registration))
            registration.Tree?.Dispose();
    }

    private readonly record struct AccessibilityRegistration(long Generation, OpenHarmonyAccessibilityTree? Tree);
}
