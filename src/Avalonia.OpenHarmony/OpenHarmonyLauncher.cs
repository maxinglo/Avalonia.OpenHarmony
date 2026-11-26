using Avalonia.Platform.Storage;

namespace Avalonia.OpenHarmony;

/// <summary>Launches supported links or files through the host UIAbility.</summary>
public sealed class OpenHarmonyLauncher(Func<string, string, Task<OpenHarmonySystemResult>> request) : ILauncher
{
    private readonly Func<string, string, Task<OpenHarmonySystemResult>> _request =
        request ?? throw new ArgumentNullException(nameof(request));

    public Task<bool> LaunchUriAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.IsFile)
            return Task.FromResult(false);
        if (uri.Scheme == "mailto")
            return LaunchAsync("launcher.mailto", uri.AbsoluteUri);
        if (uri.Host.Length > 0 && uri.AbsoluteUri.StartsWith(uri.Scheme + "://", StringComparison.Ordinal))
            return LaunchAsync("launcher.uri", uri.AbsoluteUri);
        return Task.FromResult(false);
    }

    public Task<bool> LaunchFileAsync(IStorageItem storageItem)
    {
        ArgumentNullException.ThrowIfNull(storageItem);
        var path = storageItem.Path;
        if (!path.IsAbsoluteUri || !path.IsFile)
            return Task.FromResult(false);
        if (storageItem is IStorageFolder)
            return LaunchAsync("launcher.folder", path.AbsoluteUri);
        if (storageItem is not IStorageFile)
            return Task.FromResult(false);
        return string.IsNullOrEmpty(path.Host)
            ? LaunchAsync("launcher.path", path.LocalPath)
            : LaunchAsync("launcher.file", path.AbsoluteUri);
    }

    private async Task<bool> LaunchAsync(string operation, string value)
    {
        var result = await _request(operation, value).ConfigureAwait(false);
        result.File?.Dispose();
        return result.Value is not null;
    }
}
