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
        return uri.IsAbsoluteUri && uri.Scheme is "http" or "https"
            ? LaunchAsync("launcher.uri", uri.AbsoluteUri)
            : Task.FromResult(false);
    }

    public Task<bool> LaunchFileAsync(IStorageItem storageItem)
    {
        ArgumentNullException.ThrowIfNull(storageItem);
        if (storageItem is not IStorageFile || !storageItem.Path.IsAbsoluteUri || !storageItem.Path.IsFile)
            return Task.FromResult(false);

        var path = storageItem.Path;
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
