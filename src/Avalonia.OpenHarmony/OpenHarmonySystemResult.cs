using Microsoft.Win32.SafeHandles;

namespace Avalonia.OpenHarmony;

/// <summary>Result of an ArkTS request. The receiver owns the optional file handle.</summary>
public readonly record struct OpenHarmonySystemResult(string? Value, SafeFileHandle? File);
