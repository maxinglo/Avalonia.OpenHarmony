using Avalonia.Platform.Storage;
using Microsoft.Win32.SafeHandles;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avalonia.OpenHarmony;

/// <summary>Exposes the HarmonyOS system document pickers and URI grants to Avalonia.</summary>
public sealed class OpenHarmonyStorageProvider : IStorageProvider
{
    private const int NoSuchEntry = 13900002;
    private const string BookmarkPrefix = "avalonia.ohos.storage.1:";
    private readonly Func<string, string, Task<OpenHarmonySystemResult>> _request;
    private readonly bool _canPickFolder;
    private readonly FileAccess? _persistentBookmarkAccess;
    private readonly SemaphoreSlim _picker = new(1, 1);
    private readonly SemaphoreSlim _mutations = new(1, 1);

    public OpenHarmonyStorageProvider(Func<string, string, Task<OpenHarmonySystemResult>> request, bool canPickFolder = true)
        : this(request, canPickFolder, null)
    {
    }

    /// <summary>Enables bookmark creation and reopening when the system also grants persistent file access.</summary>
    /// <param name="request">The application-thread request delegate.</param>
    /// <param name="canPickFolder">The device's folder-picker capability.</param>
    /// <param name="persistentBookmarkAccess">Read or read/write access, or null to disable bookmarks.</param>
    public OpenHarmonyStorageProvider(Func<string, string, Task<OpenHarmonySystemResult>> request,
        bool canPickFolder, FileAccess? persistentBookmarkAccess)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (persistentBookmarkAccess is not (null or FileAccess.Read or FileAccess.ReadWrite))
            throw new ArgumentOutOfRangeException(nameof(persistentBookmarkAccess));
        _request = request;
        _canPickFolder = canPickFolder;
        _persistentBookmarkAccess = persistentBookmarkAccess;
    }

    public bool CanOpen => true;
    public bool CanSave => true;
    public bool CanPickFolder => _canPickFolder;

    public async Task<IReadOnlyList<IStorageFile>> OpenFilePickerAsync(FilePickerOpenOptions options) =>
        (await OpenFilePickerWithResultAsync(options).ConfigureAwait(false)).Files;

    public async Task<OpenFilePickerResult> OpenFilePickerWithResultAsync(FilePickerOpenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var filters = GetFilters(options.FileTypeFilter, options.SuggestedFileType);
        var reply = await PickAsync("storage.select", new(
            SuggestedStart(options), options.AllowMultiple, filters.Select(filter => filter.Text).ToArray(), null
        )).ConfigureAwait(false);
        return new()
        {
            Files = reply?.Entries
                .Select(entry => TryMakeFileUri(entry.Uri, out var uri)
                    ? new StorageFile(this, uri, entry.Name, bookmarkable: entry.Bookmarkable)
                    : null)
                .OfType<IStorageFile>()
                .ToArray() ?? []
        };
    }

    public async Task<IStorageFile?> SaveFilePickerAsync(FilePickerSaveOptions options) =>
        (await SaveFilePickerWithResultAsync(options).ConfigureAwait(false)).File;

    public async Task<SaveFilePickerResult> SaveFilePickerWithResultAsync(FilePickerSaveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var filters = GetFilters(options.FileTypeChoices, options.SuggestedFileType, save: true);
        var name = options.SuggestedFileName;
        var extension = options.DefaultExtension?.TrimStart('.');
        if (!string.IsNullOrWhiteSpace(name))
        {
            ValidateChildName(name);
            if (!string.IsNullOrEmpty(extension) && extension != "*" &&
                IsSimpleSuffix("." + extension) && !System.IO.Path.HasExtension(name))
                name += "." + extension;
        }
        var reply = await PickAsync("storage.save", new(
            SuggestedStart(options), false, filters.Select(filter => filter.Text).ToArray(),
            string.IsNullOrWhiteSpace(name) ? null : name
        )).ConfigureAwait(false);
        if (reply?.Entries is not { Length: > 0 } entries || !TryMakeFileUri(entries[0].Uri, out var uri))
            return default;

        return new()
        {
            File = new StorageFile(this, uri, entries[0].Name, bookmarkable: entries[0].Bookmarkable),
            SelectedFileType = (uint)reply.SelectedIndex < (uint)filters.Count ? filters[reply.SelectedIndex].Type : null
        };
    }

    public async Task<IReadOnlyList<IStorageFolder>> OpenFolderPickerAsync(FolderPickerOpenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!CanPickFolder)
            return [];
        var reply = await PickAsync("storage.selectFolder", new(
            SuggestedStart(options), options.AllowMultiple, [], null
        )).ConfigureAwait(false);
        return reply?.Entries
            .Select(entry => TryMakeFileUri(entry.Uri, out var uri)
                ? new StorageFolder(this, uri, entry.Name, pickerDirectory: true, bookmarkable: entry.Bookmarkable)
                : null)
            .OfType<IStorageFolder>()
            .ToArray() ?? [];
    }

    public async Task<IStorageBookmarkFile?> OpenFileBookmarkAsync(string bookmark) =>
        await OpenBookmarkAsync(bookmark, directory: false).ConfigureAwait(false) as IStorageBookmarkFile;

    public async Task<IStorageBookmarkFolder?> OpenFolderBookmarkAsync(string bookmark) =>
        await OpenBookmarkAsync(bookmark, directory: true).ConfigureAwait(false) as IStorageBookmarkFolder;

    public async Task<IStorageFile?> TryGetFileFromPathAsync(Uri filePath) =>
        await GetItemAsync(filePath).ConfigureAwait(false) as IStorageFile;

    public async Task<IStorageFolder?> TryGetFolderFromPathAsync(Uri folderPath) =>
        await GetItemAsync(folderPath).ConfigureAwait(false) as IStorageFolder;

    public Task<IStorageFolder?> TryGetWellKnownFolderAsync(WellKnownFolder wellKnownFolder) =>
        Task.FromResult<IStorageFolder?>(null);

    private async Task<PickerReply?> PickAsync(string operation, PickerRequest options)
    {
        await _picker.WaitAsync().ConfigureAwait(false);
        try
        {
            var result = await _request(operation,
                JsonSerializer.Serialize(options with { RequestBookmarks = _persistentBookmarkAccess.HasValue },
                    StorageJson.Default.PickerRequest)).ConfigureAwait(false);
            using var unexpectedFile = result.File;
            return result.Value is { } value ? JsonSerializer.Deserialize(value, StorageJson.Default.PickerReply) : null;
        }
        finally
        {
            _picker.Release();
        }
    }

    private async Task<StorageItem?> GetItemAsync(Uri uri, StorageFolder? mutationParent = null, string? entryName = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !uri.IsFile)
            return null;
        var stat = await StatAsync(uri).ConfigureAwait(false);
        if (stat is null)
            return null;
        if (stat.Directory)
            return new StorageFolder(this, uri, stat.Name, mutationParent, entryName);
        return new StorageFile(this, uri, stat.Name, mutationParent, entryName);
    }

    private async Task<StorageItem?> OpenBookmarkAsync(string bookmark, bool directory)
    {
        if (_persistentBookmarkAccess is null || bookmark is null ||
            !bookmark.StartsWith(BookmarkPrefix, StringComparison.Ordinal))
            return null;

        BookmarkRecord? record;
        try
        {
            record = JsonSerializer.Deserialize(bookmark.AsSpan(BookmarkPrefix.Length), StorageJson.Default.BookmarkRecord);
        }
        catch (JsonException)
        {
            return null;
        }

        if (record is null || record.Directory != directory || record.Access is not (FileAccess.Read or FileAccess.ReadWrite) ||
            (record.Access & _persistentBookmarkAccess.Value) != record.Access || !TryMakeFileUri(record.Uri, out var uri))
            return null;
        if (await RequestBookmarkAsync("storage.bookmark.activate", record).ConfigureAwait(false) is not { Error: 0 })
            return null;

        try
        {
            if (await StatAsync(uri).ConfigureAwait(false) is not { } info || info.Directory != directory)
                return null;
            return directory
                ? new StorageFolder(this, uri, info.Name, bookmarkable: true, savedBookmark: record)
                : new StorageFile(this, uri, info.Name, bookmarkable: true, savedBookmark: record);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<BookmarkReply?> RequestBookmarkAsync(string operation, BookmarkRecord bookmark)
    {
        var result = await _request(operation,
            JsonSerializer.Serialize(bookmark, StorageJson.Default.BookmarkRecord)).ConfigureAwait(false);
        using var unexpectedFile = result.File;
        return result.Value is { } value ? JsonSerializer.Deserialize(value, StorageJson.Default.BookmarkReply) : null;
    }

    private async Task<StatReply?> StatAsync(Uri uri)
    {
        var result = await _request("storage.stat", uri.OriginalString).ConfigureAwait(false);
        using var unexpectedFile = result.File;
        var stat = result.Value is { } value ? JsonSerializer.Deserialize(value, StorageJson.Default.StatReply) : null;
        if (stat?.Error == NoSuchEntry)
            return null;
        if (stat is { Error: not 0 } failure)
            ThrowFileError(failure.Error);
        return stat;
    }

    private async Task<SafeFileHandle> OpenDescriptorAsync(string operation, Uri uri)
    {
        var result = await _request(operation, uri.OriginalString).ConfigureAwait(false);
        if (result.File is { IsInvalid: false, IsClosed: false } file)
            return file;

        result.File?.Dispose();
        if (result.Value is { Length: > 0 } errorJson &&
            JsonSerializer.Deserialize(errorJson, StorageJson.Default.StatReply) is { Error: not 0 } failure)
            ThrowFileError(failure.Error);
        throw new IOException("The HarmonyOS file service did not provide a descriptor.");
    }

    private static void ThrowFileError(int error)
    {
        throw error switch
        {
            13900001 or 13900012 or 201 => new UnauthorizedAccessException($"HarmonyOS denied access to this item ({error})."),
            NoSuchEntry => new FileNotFoundException("The selected HarmonyOS file is no longer available."),
            _ => new IOException($"HarmonyOS file service error {error}.")
        };
    }

    private static string? SuggestedStart(PickerOptions options) =>
        options.SuggestedStartLocation?.Path is { IsAbsoluteUri: true, IsFile: true } uri ? uri.OriginalString : null;

    private static bool TryMakeFileUri(string value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.IsFile;

    internal IStorageFile? RetainDroppedFile(string value, int descriptor, ulong length)
    {
        if (value.Length > 4096 || !value.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            value.Any(char.IsControl) || !TryMakeFileUri(value, out var uri) ||
            uri.Segments.Length < 2 || uri.AbsolutePath.EndsWith('/') ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            return null;

        var retained = OpenHarmonyDropFile.Retain(descriptor);
        return retained is null ? null : new OpenHarmonyDropFile(uri, retained, length);
    }

    private static List<(string Text, FilePickerFileType Type)> GetFilters(
        IReadOnlyList<FilePickerFileType>? choices,
        FilePickerFileType? suggested,
        bool save = false)
    {
        var filters = new List<(string Text, FilePickerFileType Type)>();
        if (choices is null)
            return filters;

        foreach (var type in choices.OrderByDescending(type => ReferenceEquals(type, suggested)).Take(100))
        {
            var suffixes = type.Patterns?
                .Where(pattern => pattern.Length > 2 && pattern[0] == '*' && pattern[1] == '.')
                .Select(pattern => pattern[1..])
                .Where(IsSimpleSuffix)
                .Where(suffix => !save || suffix != ".*")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(save ? 1 : 100)
                .ToArray();
            if (suffixes is not { Length: > 0 })
                continue;
            var name = type.Name.Replace('|', ' ');
            var suffixFilter = string.Join(',', suffixes);
            filters.Add((name.Length == 0 ? suffixFilter : $"{name}|{suffixFilter}", type));
        }
        return filters;
    }

    private static bool IsSimpleSuffix(string suffix) => suffix == ".*" ||
        (suffix.Length > 1 && suffix[0] == '.' &&
            suffix.AsSpan(1).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".AsSpan()) < 0);

    private static void ValidateChildName(string name)
    {
        if (!IsChildName(name))
            throw new ArgumentException("Expected one file or folder name.", nameof(name));
    }

    private static bool IsChildName(string name) =>
        !string.IsNullOrEmpty(name) && name is not "." and not ".." && !name.AsSpan().ContainsAny('/', '\\', '\0');

    private abstract class StorageItem(OpenHarmonyStorageProvider owner, Uri path, string? name,
        StorageFolder? mutationParent, string? entryName, bool bookmarkable, BookmarkRecord? savedBookmark)
        : IStorageBookmarkItem
    {
        private readonly FileAccess? _reopenedAccess = savedBookmark?.Access;
        private volatile BookmarkRecord? _savedBookmark = savedBookmark;
        private volatile bool _removed;
        protected OpenHarmonyStorageProvider Owner { get; } = owner;
        protected StorageFolder? MutationParent { get; } = mutationParent;
        protected string EntryName { get; } = entryName ?? string.Empty;
        protected bool IsRemoved => _removed;
        protected bool ReadOnlyBookmarkScope =>
            _reopenedAccess == FileAccess.Read || MutationParent?.ReadOnlyBookmarkScope == true;
        public Uri Path { get; } = path;
        public string Name { get; } = string.IsNullOrEmpty(name)
            ? Uri.UnescapeDataString(path.Segments.Last().TrimEnd('/'))
            : name;
        public bool CanBookmark => Owner._persistentBookmarkAccess.HasValue && bookmarkable && !_removed &&
            (_reopenedAccess is null || _savedBookmark is not null);

        public async Task<StorageItemProperties> GetBasicPropertiesAsync()
        {
            var stat = await Owner.StatAsync(Path).ConfigureAwait(false);
            return stat is null ? new() : new(stat.Size, dateModified: stat.ModifiedSeconds is { } time
                ? DateTimeOffset.FromUnixTimeSeconds(time)
                : null);
        }

        public async Task<IStorageFolder?> GetParentAsync()
        {
            if (MutationParent is not null)
                return MutationParent;
            var directory = this is IStorageFolder ? new Uri(Path.AbsoluteUri.TrimEnd('/') + "/") : Path;
            var parent = new Uri(directory, this is IStorageFolder ? ".." : ".");
            if (directory == parent)
                return null;
            try
            {
                return await Owner.TryGetFolderFromPathAsync(parent).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                return null; // A grant for one picked file does not authorize its parent.
            }
        }

        public async Task<string?> SaveBookmarkAsync()
        {
            if (Owner._persistentBookmarkAccess is not { } access || !CanBookmark)
                return null;
            await Owner._mutations.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_removed)
                    return null;
                var record = _savedBookmark ?? new BookmarkRecord(Path.OriginalString, access, this is IStorageFolder);
                if (await Owner.RequestBookmarkAsync("storage.bookmark.persist", record).ConfigureAwait(false) is not { Error: 0 })
                    return null;
                _savedBookmark = record;
                return BookmarkPrefix + JsonSerializer.Serialize(record, StorageJson.Default.BookmarkRecord);
            }
            finally
            {
                Owner._mutations.Release();
            }
        }

        public async Task ReleaseBookmarkAsync()
        {
            await Owner._mutations.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_savedBookmark is not { } record)
                    return;
                var result = await Owner.RequestBookmarkAsync("storage.bookmark.revoke", record).ConfigureAwait(false);
                if (result is not { Error: 0 })
                {
                    if (result?.Error is 201 or 13900001 or 13900012)
                        throw new UnauthorizedAccessException($"HarmonyOS denied bookmark revocation ({result.Error}, policy {result.PolicyError}).");
                    throw new IOException($"HarmonyOS could not revoke the bookmark ({result?.Error}, policy {result?.PolicyError}).");
                }
                _savedBookmark = null;
            }
            finally
            {
                Owner._mutations.Release();
            }
        }

        public async Task DeleteAsync()
        {
            if (_removed)
                return;
            if (MutationParent is not { CanMutateChildren: true } parent)
                throw new UnauthorizedAccessException("Deleting an item requires its containing directory's picker grant.");
            await Owner._mutations.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_removed)
                    return;
                using var directory = await parent.OpenMutationDirectoryAsync().ConfigureAwait(false);
                await Task.Run(() => StorageDirectory.DeleteChild(directory, EntryName, this is IStorageFolder)).ConfigureAwait(false);
                _removed = true;
            }
            finally
            {
                Owner._mutations.Release();
            }
        }

        public async Task<IStorageItem?> MoveAsync(IStorageFolder destination)
        {
            if (MutationParent is not { CanMutateChildren: true } source ||
                destination is not StorageFolder { CanMutateChildren: true } target ||
                !ReferenceEquals(target.Owner, Owner))
                return null;

            await Owner._mutations.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_removed)
                    return null;
                if (string.Equals(source.Path.AbsoluteUri.TrimEnd('/'), target.Path.AbsoluteUri.TrimEnd('/'),
                    StringComparison.Ordinal))
                    return this;
                var newPath = target.ChildUri(EntryName);
                using var sourceDirectory = await source.OpenMutationDirectoryAsync().ConfigureAwait(false);
                using var targetDirectory = await target.OpenMutationDirectoryAsync().ConfigureAwait(false);
                if (!await Task.Run(() => StorageDirectory.TryMoveChild(sourceDirectory, targetDirectory, EntryName))
                        .ConfigureAwait(false))
                    return null;
                _removed = true;
                return this is IStorageFolder
                    ? new StorageFolder(Owner, newPath, Name, target, EntryName)
                    : new StorageFile(Owner, newPath, Name, target, EntryName);
            }
            finally
            {
                Owner._mutations.Release();
            }
        }

        public void Dispose() { } // Only a stream or directory enumeration owns a native descriptor.

        protected void EnsureWritable()
        {
            if (_removed)
                throw new FileNotFoundException("The selected HarmonyOS item was moved or deleted.");
            if (ReadOnlyBookmarkScope)
                throw new UnauthorizedAccessException("This item was reopened with a read-only bookmark.");
        }
    }

    private sealed class StorageFile(OpenHarmonyStorageProvider owner, Uri path, string? name,
        StorageFolder? mutationParent = null, string? entryName = null, bool bookmarkable = false, BookmarkRecord? savedBookmark = null)
        : StorageItem(owner, path, name, mutationParent, entryName, bookmarkable, savedBookmark), IStorageBookmarkFile
    {
        public Task<Stream> OpenReadAsync() => OpenAsync("storage.openRead", FileAccess.Read);
        public Task<Stream> OpenWriteAsync() => OpenAsync("storage.openWrite", FileAccess.Write);

        private async Task<Stream> OpenAsync(string operation, FileAccess access)
        {
            if (access == FileAccess.Write)
                EnsureWritable();
            var file = await Owner.OpenDescriptorAsync(operation, Path).ConfigureAwait(false);
            try
            {
                return new FileStream(file, access, bufferSize: 4096, isAsync: false);
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }
    }

    private sealed class StorageFolder(OpenHarmonyStorageProvider owner, Uri path, string? name,
        StorageFolder? mutationParent = null, string? entryName = null, bool pickerDirectory = false,
        bool bookmarkable = false, BookmarkRecord? savedBookmark = null)
        : StorageItem(owner, path, name, mutationParent, entryName, bookmarkable, savedBookmark), IStorageBookmarkFolder
    {
        public bool CanMutateChildren => !IsRemoved && (pickerDirectory || MutationParent?.CanMutateChildren == true);

        public async Task<SafeFileHandle> OpenMutationDirectoryAsync()
        {
            if (IsRemoved)
                throw new DirectoryNotFoundException("The selected HarmonyOS directory was moved or deleted.");
            if (pickerDirectory)
                return await Owner.OpenDescriptorAsync("storage.openMutationDirectory", Path).ConfigureAwait(false);
            if (MutationParent is null)
                throw new UnauthorizedAccessException("This directory was not acquired from the system folder picker.");
            using var parent = await MutationParent.OpenMutationDirectoryAsync().ConfigureAwait(false);
            return await Task.Run(() => StorageDirectory.OpenChildDirectory(parent, EntryName)).ConfigureAwait(false);
        }

        public async IAsyncEnumerable<IStorageItem> GetItemsAsync()
        {
            using var descriptor = CanMutateChildren
                ? await OpenMutationDirectoryAsync().ConfigureAwait(false)
                : await Owner.OpenDescriptorAsync("storage.openRead", Path).ConfigureAwait(false);
            foreach (var entry in await Task.Run(() => StorageDirectory.Enumerate(descriptor)).ConfigureAwait(false))
            {
                if (!IsChildName(entry.Name))
                    continue;
                var uri = ChildUri(entry.Name);
                if (entry.Kind == StorageDirectory.EntryKind.Directory)
                    yield return new StorageFolder(Owner, uri, entry.Name, this, entry.Name);
                else if (entry.Kind == StorageDirectory.EntryKind.File)
                    yield return new StorageFile(Owner, uri, entry.Name, this, entry.Name);
                else if (await Owner.GetItemAsync(uri, this, entry.Name).ConfigureAwait(false) is { } item)
                    yield return item;
            }
        }

        public async Task<IStorageFolder?> GetFolderAsync(string name) =>
            await Owner.GetItemAsync(ChildUri(name), this, name).ConfigureAwait(false) as IStorageFolder;

        public async Task<IStorageFile?> GetFileAsync(string name) =>
            await Owner.GetItemAsync(ChildUri(name), this, name).ConfigureAwait(false) as IStorageFile;

        public async Task<IStorageFile?> CreateFileAsync(string name)
        {
            EnsureWritable();
            var uri = ChildUri(name);
            using var descriptor = await Owner.OpenDescriptorAsync("storage.createFile", uri).ConfigureAwait(false);
            return new StorageFile(Owner, uri, name, this, name);
        }

        public async Task<IStorageFolder?> CreateFolderAsync(string name)
        {
            EnsureWritable();
            var uri = ChildUri(name);
            using var descriptor = await Owner.OpenDescriptorAsync("storage.openRead", Path).ConfigureAwait(false);
            await Task.Run(() => StorageDirectory.CreateSubdirectory(descriptor, name)).ConfigureAwait(false);
            return await Owner.GetItemAsync(uri, this, name).ConfigureAwait(false) as IStorageFolder;
        }

        public Uri ChildUri(string name)
        {
            ValidateChildName(name);
            return new Uri(new Uri(Path.AbsoluteUri.TrimEnd('/') + "/"), Uri.EscapeDataString(name));
        }
    }
}

internal sealed record PickerRequest(string? StartUri, bool Multiple, string[] Filters, string? FileName, bool RequestBookmarks = false);
internal sealed record PickerEntry(string Uri, string Name, bool Bookmarkable = false);
internal sealed record PickerReply(PickerEntry[] Entries, int SelectedIndex);
internal sealed record StatReply(int Error, string? Name, bool Directory, ulong? Size, long? ModifiedSeconds);
internal sealed record BookmarkRecord(string Uri, FileAccess Access, bool Directory);
internal sealed record BookmarkReply(int Error, int? PolicyError);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PickerRequest))]
[JsonSerializable(typeof(PickerReply))]
[JsonSerializable(typeof(StatReply))]
[JsonSerializable(typeof(BookmarkRecord))]
[JsonSerializable(typeof(BookmarkReply))]
internal sealed partial class StorageJson : JsonSerializerContext;
