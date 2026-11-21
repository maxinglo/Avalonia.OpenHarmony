namespace Avalonia.OpenHarmony;

/// <summary>Enables input route logs without recording the contents of text fields.</summary>
public sealed class OpenHarmonyInputDiagnostics(Action<string> write)
{
    private readonly Action<string> _write = write ?? throw new ArgumentNullException(nameof(write));

    internal void Report(string message)
    {
        try { _write(message); }
        catch (Exception)
        {
            // The observer must not interrupt input dispatch.
        }
    }
}
