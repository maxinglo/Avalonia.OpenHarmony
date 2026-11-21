using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace Avalonia.OpenHarmony;

internal static unsafe partial class StorageDirectory
{
    private const string LibC = "libc.so";
    private const int NameOffset = 19; // offsetof(dirent, d_name), identical in the two supported API 26 SDK ABIs.
    private const int OwnerReadWriteExecute = 448; // POSIX mode 0700.

    internal enum EntryKind : byte
    {
        Unknown,
        Directory = 4,
        File = 8
    }

    internal readonly record struct Entry(string Name, EntryKind Kind);

    public static IReadOnlyList<Entry> Enumerate(SafeFileHandle descriptor)
    {
        var directory = FdOpenDir(checked((int)descriptor.DangerousGetHandle()));
        if (directory == IntPtr.Zero)
            throw DirectoryError("fdopendir", Marshal.GetLastPInvokeError());
        descriptor.SetHandleAsInvalid(); // closedir owns the descriptor from here.

        var entries = new List<Entry>();
        try
        {
            while (ReadDir(directory) is var entry && entry != null)
            {
                var byteCount = Math.Clamp(entry->RecordLength - NameOffset, 0, 256);
                var name = new ReadOnlySpan<byte>(entry->Name, byteCount);
                var terminator = name.IndexOf((byte)0);
                if (terminator > 0)
                {
                    var text = Encoding.UTF8.GetString(name[..terminator]);
                    if (text is not "." and not "..")
                        entries.Add(new(text, entry->Type));
                }
            }
            var error = Marshal.GetLastPInvokeError();
            if (error != 0)
                throw DirectoryError("readdir", error);
            return entries;
        }
        finally
        {
            CloseDir(directory);
        }
    }

    public static void CreateSubdirectory(SafeFileHandle descriptor, string name)
    {
        if (MakeDirectoryAt(checked((int)descriptor.DangerousGetHandle()), name, OwnerReadWriteExecute) == 0)
            return;
        var error = Marshal.GetLastPInvokeError();
        if (error != 17) // EEXIST. The caller subsequently checks the item's type.
            throw DirectoryError("mkdirat", error);
    }

    private static Exception DirectoryError(string operation, int error) => error is 1 or 13
        ? new UnauthorizedAccessException($"HarmonyOS denied the directory operation {operation} ({error}).")
        : new IOException($"The HarmonyOS directory operation {operation} failed ({error}).");

    [StructLayout(LayoutKind.Explicit)]
    private struct DirectoryEntry
    {
        [FieldOffset(16)] public ushort RecordLength;
        [FieldOffset(18)] public EntryKind Type;
        [FieldOffset(NameOffset)] public fixed byte Name[256];
    }

    [LibraryImport(LibC, EntryPoint = "fdopendir", SetLastError = true)]
    private static partial nint FdOpenDir(int descriptor);

    [LibraryImport(LibC, EntryPoint = "readdir", SetLastError = true)]
    private static partial DirectoryEntry* ReadDir(nint directory);

    [LibraryImport(LibC, EntryPoint = "closedir")]
    private static partial int CloseDir(nint directory);

    [LibraryImport(LibC, EntryPoint = "mkdirat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int MakeDirectoryAt(int descriptor, string name, uint mode);
}
