using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace Avalonia.OpenHarmony;

internal static unsafe partial class StorageDirectory
{
    private const string LibC = "libc.so";
    private const int NameOffset = 19; // offsetof(dirent, d_name), identical in the two supported API 26 SDK ABIs.
    private const int OwnerReadWriteExecute = 448; // POSIX mode 0700.
    private const int CloseOnExec = 0x80000;
    private const int RemoveDirectory = 0x200; // AT_REMOVEDIR.
    private const uint RenameNoReplace = 1;
    private static readonly Encoding FilenameEncoding = new UTF8Encoding(false, true);

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
                    try
                    {
                        var text = FilenameEncoding.GetString(name[..terminator]);
                        if (text is not "." and not "..")
                            entries.Add(new(text, entry->Type));
                    }
                    catch (DecoderFallbackException)
                    {
                        // A name outside UTF-8 cannot be round-tripped as a HarmonyOS URI.
                    }
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

    public static SafeFileHandle OpenChildDirectory(SafeFileHandle parent, string name)
    {
        // The HarmonyOS SDK uses the architecture's POSIX O_DIRECTORY and O_NOFOLLOW flags.
        var flags = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => CloseOnExec | 0x10000 | 0x20000,
            Architecture.Arm64 => CloseOnExec | 0x4000 | 0x8000,
            _ => throw new PlatformNotSupportedException("The HarmonyOS storage ABI supports x86_64 and arm64.")
        };
        var fd = OpenAt(checked((int)parent.DangerousGetHandle()), name, flags);
        if (fd < 0)
            throw DirectoryError("openat", Marshal.GetLastPInvokeError());
        return new SafeFileHandle((nint)fd, ownsHandle: true);
    }

    public static void DeleteChild(SafeFileHandle parent, string name, bool directory)
    {
        if (UnlinkAt(checked((int)parent.DangerousGetHandle()), name, directory ? RemoveDirectory : 0) == 0)
            return;
        var error = Marshal.GetLastPInvokeError();
        if (error != 2) // ENOENT: it is already absent.
            throw DirectoryError("unlinkat", error);
    }

    public static bool TryMoveChild(SafeFileHandle source, SafeFileHandle target, string name)
    {
        if (RenameAt2(checked((int)source.DangerousGetHandle()), name, checked((int)target.DangerousGetHandle()), name,
            RenameNoReplace) == 0)
            return true;
        var error = Marshal.GetLastPInvokeError();
        if (error is 18 or 38 or 95) // EXDEV, ENOSYS or EOPNOTSUPP. A copy/delete fallback would not be atomic.
            return false;
        throw DirectoryError("renameat2", error);
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

    [LibraryImport(LibC, EntryPoint = "openat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenAt(int descriptor, string name, int flags);

    [LibraryImport(LibC, EntryPoint = "unlinkat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int UnlinkAt(int descriptor, string name, int flags);

    [LibraryImport(LibC, EntryPoint = "renameat2", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int RenameAt2(int source, string sourceName, int target, string targetName, uint flags);
}
