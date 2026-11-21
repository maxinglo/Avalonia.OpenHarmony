using System.Runtime.InteropServices;
using Avalonia.Platform.Storage;
using Microsoft.Win32.SafeHandles;

namespace Avalonia.OpenHarmony;

internal sealed partial class OpenHarmonyDropFile(Uri uri, SafeFileHandle retained, ulong size) : IStorageFile
{
    public string Name { get; } = Uri.UnescapeDataString(uri.Segments[^1]);
    public Uri Path { get; } = uri;
    public bool CanBookmark => false;

    public Task<StorageItemProperties> GetBasicPropertiesAsync() =>
        Task.FromResult(new StorageItemProperties(size));

    public Task<Stream> OpenReadAsync()
    {
        // A stream may outlive the storage item. Its duplicate descriptor has an independent lifetime.
        var addedReference = false;
        try
        {
            retained.DangerousAddRef(ref addedReference);
            var duplicate = Retain(checked((int)retained.DangerousGetHandle()));
            if (duplicate is null)
                return Task.FromException<Stream>(new IOException("Could not retain the dropped file."));
            return Task.FromResult<Stream>(new DropReadStream(duplicate));
        }
        finally
        {
            if (addedReference)
                retained.DangerousRelease();
        }
    }

    public Task<Stream> OpenWriteAsync() => Task.FromException<Stream>(ReadOnlyError());
    public Task DeleteAsync() => Task.FromException(ReadOnlyError());
    public Task<IStorageItem?> MoveAsync(IStorageFolder destination) => Task.FromException<IStorageItem?>(ReadOnlyError());
    public Task<IStorageFolder?> GetParentAsync() => Task.FromResult<IStorageFolder?>(null);
    public Task<string?> SaveBookmarkAsync() => Task.FromResult<string?>(null);
    public void Dispose() => retained.Dispose();

    internal static SafeFileHandle? Retain(int descriptor)
    {
        if (descriptor < 0)
            return null;
        // HarmonyOS SDK fcntl.h, F_DUPFD_CLOEXEC. Start at 3 to skip the standard descriptors.
        var duplicate = DuplicateDescriptor(descriptor, 1030, 3);
        return duplicate < 0 ? null : new SafeFileHandle(duplicate, ownsHandle: true);
    }

    private static UnauthorizedAccessException ReadOnlyError() => new("The dropped file is available for reading only.");

    [LibraryImport("libc.so", EntryPoint = "fcntl")]
    private static partial int DuplicateDescriptor(int descriptor, int operation, int minimum);

    private sealed class DropReadStream(SafeFileHandle handle) : Stream
    {
        private long _position;

        public override bool CanRead => !handle.IsClosed;
        public override bool CanSeek => !handle.IsClosed;
        public override bool CanWrite => false;
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

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                handle.Dispose();
            base.Dispose(disposing);
        }
    }
}
