using Avalonia.Platform.Storage;
using Microsoft.Win32.SafeHandles;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avalonia.OpenHarmony;

/// <summary>Exposes the HarmonyOS system document pickers and their temporary URI grants to Avalonia.</summary>
public sealed class OpenHarmonyStorageProvider(
    Func<string, string, Task<OpenHarmonySystemResult>> request,
    bool canPickFolder = true) : IStorageProvider
{
    private const int NoSuchEntry = 13900002;
    private readonly Func<string, string, Task<OpenHarmonySystemResult>> _request =
        request ?? throw new ArgumentNullException(nameof(request));
    private readonly SemaphoreSlim _picker = new(1, 1);

    public bool CanOpen => true;
    public bool CanSave => true;
    public bool CanPickFolder => canPickFolder;

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
                .Select(entry => TryMakeFileUri(entry.Uri, out var uri) ? new StorageFile(this, uri, entry.Name) : null)
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
            File = new StorageFile(this, uri, entries[0].Name),
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
            .Select(entry => TryMakeFileUri(entry.Uri, out var uri) ? new StorageFolder(this, uri, entry.Name) : null)
            .OfType<IStorageFolder>()
            .ToArray() ?? [];
    }

    public Task<IStorageBookmarkFile?> OpenFileBookmarkAsync(string bookmark) =>
        Task.FromResult<IStorageBookmarkFile?>(null);

    public Task<IStorageBookmarkFolder?> OpenFolderBookmarkAsync(string bookmark) =>
        Task.FromResult<IStorageBookmarkFolder?>(null);

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
                JsonSerializer.Serialize(options, StorageJson.Default.PickerRequest)).ConfigureAwait(false);
            using var unexpectedFile = result.File;
            return result.Value is { } value ? JsonSerializer.Deserialize(value, StorageJson.Default.PickerReply) : null;
        }
        finally
        {
            _picker.Release();
        }
    }

    private async Task<StorageItem?> GetItemAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !uri.IsFile)
            return null;
        var stat = await StatAsync(uri).ConfigureAwait(false);
        if (stat is null)
            return null;
        if (stat.Directory)
            return new StorageFolder(this, uri, stat.Name);
        return new StorageFile(this, uri, stat.Name);
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
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.AsSpan().ContainsAny('/', '\\', '\0'))
            throw new ArgumentException("Expected one file or folder name.", nameof(name));
    }

    private abstract class StorageItem(OpenHarmonyStorageProvider owner, Uri path, string? name) : IStorageItem
    {
        protected OpenHarmonyStorageProvider Owner { get; } = owner;
        public Uri Path { get; } = path;
        public string Name { get; } = string.IsNullOrEmpty(name)
            ? Uri.UnescapeDataString(path.Segments.Last().TrimEnd('/'))
            : name;
        public bool CanBookmark => false;

        public async Task<StorageItemProperties> GetBasicPropertiesAsync()
        {
            var stat = await Owner.StatAsync(Path).ConfigureAwait(false);
            return stat is null ? new() : new(stat.Size, dateModified: stat.ModifiedSeconds is { } time
                ? DateTimeOffset.FromUnixTimeSeconds(time)
                : null);
        }

        public async Task<IStorageFolder?> GetParentAsync()
        {
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

        public Task<string?> SaveBookmarkAsync() => Task.FromResult<string?>(null);
        public Task DeleteAsync() => Task.FromException(new NotSupportedException("Deleting through the document picker is unavailable."));
        public Task<IStorageItem?> MoveAsync(IStorageFolder destination) => Task.FromResult<IStorageItem?>(null);
        public void Dispose() { } // Only a stream or directory enumeration owns a native descriptor.
    }

    private sealed class StorageFile(OpenHarmonyStorageProvider owner, Uri path, string? name)
        : StorageItem(owner, path, name), IStorageFile
    {
        public Task<Stream> OpenReadAsync() => OpenAsync("storage.openRead", FileAccess.Read);
        public Task<Stream> OpenWriteAsync() => OpenAsync("storage.openWrite", FileAccess.Write);

        private async Task<Stream> OpenAsync(string operation, FileAccess access)
        {
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

    private sealed class StorageFolder(OpenHarmonyStorageProvider owner, Uri path, string? name)
        : StorageItem(owner, path, name), IStorageFolder
    {
        public async IAsyncEnumerable<IStorageItem> GetItemsAsync()
        {
            using var descriptor = await Owner.OpenDescriptorAsync("storage.openRead", Path).ConfigureAwait(false);
            foreach (var entry in await Task.Run(() => StorageDirectory.Enumerate(descriptor)).ConfigureAwait(false))
            {
                var uri = ChildUri(entry.Name);
                if (entry.Kind == StorageDirectory.EntryKind.Directory)
                    yield return new StorageFolder(Owner, uri, entry.Name);
                else if (entry.Kind == StorageDirectory.EntryKind.File)
                    yield return new StorageFile(Owner, uri, entry.Name);
                else if (await Owner.GetItemAsync(uri).ConfigureAwait(false) is { } item)
                    yield return item;
            }
        }

        public Task<IStorageFolder?> GetFolderAsync(string name) => Owner.TryGetFolderFromPathAsync(ChildUri(name));
        public Task<IStorageFile?> GetFileAsync(string name) => Owner.TryGetFileFromPathAsync(ChildUri(name));

        public async Task<IStorageFile?> CreateFileAsync(string name)
        {
            var uri = ChildUri(name);
            using var descriptor = await Owner.OpenDescriptorAsync("storage.createFile", uri).ConfigureAwait(false);
            return new StorageFile(Owner, uri, name);
        }

        public async Task<IStorageFolder?> CreateFolderAsync(string name)
        {
            var uri = ChildUri(name);
            using var descriptor = await Owner.OpenDescriptorAsync("storage.openRead", Path).ConfigureAwait(false);
            await Task.Run(() => StorageDirectory.CreateSubdirectory(descriptor, name)).ConfigureAwait(false);
            return await Owner.TryGetFolderFromPathAsync(uri).ConfigureAwait(false);
        }

        private Uri ChildUri(string name)
        {
            ValidateChildName(name);
            return new Uri(new Uri(Path.AbsoluteUri.TrimEnd('/') + "/"), Uri.EscapeDataString(name));
        }
    }
}

internal sealed record PickerRequest(string? StartUri, bool Multiple, string[] Filters, string? FileName);
internal sealed record PickerEntry(string Uri, string Name);
internal sealed record PickerReply(PickerEntry[] Entries, int SelectedIndex);
internal sealed record StatReply(int Error, string? Name, bool Directory, ulong? Size, long? ModifiedSeconds);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PickerRequest))]
[JsonSerializable(typeof(PickerReply))]
[JsonSerializable(typeof(StatReply))]
internal sealed partial class StorageJson : JsonSerializerContext;
