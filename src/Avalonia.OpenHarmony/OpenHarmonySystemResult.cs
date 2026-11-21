using Microsoft.Win32.SafeHandles;

namespace Avalonia.OpenHarmony;

/// <summary>Result of an operation on the HarmonyOS application thread.</summary>
/// <param name="Value">The result payload, or null if the operation was canceled or unavailable.</param>
/// <param name="File">An optional owned file descriptor. The receiver is responsible for releasing it.</param>
public readonly record struct OpenHarmonySystemResult(string? Value, SafeFileHandle? File);
