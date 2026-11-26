using System.Runtime.InteropServices;
using Avalonia.Platform.Storage;
using Microsoft.Win32.SafeHandles;

namespace Avalonia.OpenHarmony;

internal sealed partial class OpenHarmonyDropFile(Uri uri, SafeFileHandle retained, ulong size, bool writable) : IStorageFile
{
    public string Name { get; } = Uri.UnescapeDataString(uri.Segments[^1]);
    public Uri Path { get; } = uri;
    public bool CanBookmark => false;

    public Task<StorageItemProperties> GetBasicPropertiesAsync() =>
        Task.FromResult(new StorageItemProperties(writable ? checked((ulong)RandomAccess.GetLength(retained)) : size));

    public Task<Stream> OpenReadAsync() => OpenStreamAsync(write: false);

    public Task<Stream> OpenWriteAsync() => writable
        ? OpenStreamAsync(write: true)
        : Task.FromException<Stream>(ReadOnlyError());

    private Task<Stream> OpenStreamAsync(bool write)
    {
        // A stream may outlive the storage item. Its duplicate descriptor has an independent lifetime.
        var addedReference = false;
        try
        {
            retained.DangerousAddRef(ref addedReference);
            var duplicate = Retain(checked((int)retained.DangerousGetHandle()));
            if (duplicate is null)
                return Task.FromException<Stream>(new IOException("Could not retain the dropped file."));
            return Task.FromResult<Stream>(new DropStream(duplicate, write));
        }
        finally
        {
            if (addedReference)
                retained.DangerousRelease();
        }
    }

    public Task DeleteAsync() => Task.FromException(MutationError());
    public Task<IStorageItem?> MoveAsync(IStorageFolder destination) => Task.FromException<IStorageItem?>(MutationError());
    public Task<IStorageFolder?> GetParentAsync() => Task.FromResult<IStorageFolder?>(null);
    public Task<string?> SaveBookmarkAsync() => Task.FromResult<string?>(null);
    public void Dispose() => retained.Dispose();

    internal static SafeFileHandle? Retain(int descriptor)
    {
        if (descriptor < 0)
            return null;
        // HarmonyOS SDK fcntl.h, F_DUPFD_CLOEXEC. Start at 3 to skip the standard descriptors.
        var duplicate = FileControl(descriptor, 1030, 3);
        return duplicate < 0 ? null : new SafeFileHandle(duplicate, ownsHandle: true);
    }

    internal static bool CanWrite(SafeFileHandle handle)
    {
        // Inspect the two read/write mode bits reported by the SDK's F_GETFL command for a regular file.
        var flags = FileControl(checked((int)handle.DangerousGetHandle()), 3, 0);
        return flags >= 0 && (flags & 3) == 2;
    }

    private static UnauthorizedAccessException ReadOnlyError() => new("The dropped file is available for reading only.");
    private static UnauthorizedAccessException MutationError() => new("The drop did not authorize directory changes.");

    [LibraryImport("libc.so", EntryPoint = "fcntl")]
    private static partial int FileControl(int descriptor, int operation, int value);

    private sealed class DropStream(SafeFileHandle handle, bool write) : Stream
    {
        private long _position;

        public override bool CanRead => !handle.IsClosed;
        public override bool CanSeek => !handle.IsClosed;
        public override bool CanWrite => write && !handle.IsClosed;
        public override long Length => RandomAccess.GetLength(handle);

        public override long Position
        {
            get => _position;
            set => _position = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            // Duplicated descriptors share a kernel file position. Positioned reads keep streams independent.
            var result = RandomAccess.Read(handle, buffer, _position);
            _position += result;
            return result;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var result = await RandomAccess.ReadAsync(handle, buffer, _position, cancellationToken).ConfigureAwait(false);
            _position += result;
            return result;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin)
        {
            var start = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => Position,
                SeekOrigin.End => Length,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            var position = checked(start + offset);
            if (position < 0)
                throw new IOException("Cannot seek before the start of the dropped file.");
            return Position = position;
        }

        public override void Flush()
        {
            if (write)
                RandomAccess.FlushToDisk(handle);
        }

        public override void SetLength(long value)
        {
            EnsureWritable();
            RandomAccess.SetLength(handle, value);
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureWritable();
            RandomAccess.Write(handle, buffer, _position);
            _position += buffer.Length;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            EnsureWritable();
            await RandomAccess.WriteAsync(handle, buffer, _position, cancellationToken).ConfigureAwait(false);
            _position += buffer.Length;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private void EnsureWritable()
        {
            if (!write)
                throw new NotSupportedException("The stream is read-only.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                handle.Dispose();
            base.Dispose(disposing);
        }
    }
}
