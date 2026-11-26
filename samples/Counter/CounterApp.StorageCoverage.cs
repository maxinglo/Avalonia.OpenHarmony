using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Text;

namespace Counter;

public sealed partial class CounterApp
{
    private static readonly FilePickerFileType MimeText = new("Plain text") { MimeTypes = ["text/plain"] };
    private static readonly FilePickerFileType MimeXml = new("XML documents") { MimeTypes = ["application/xml"] };

    private static (Control Panel, (string Name, Control Element)[] Targets) CreateStorageCoverage(
        Control owner, Action<string> report)
    {
        var panel = new WrapPanel();
        var targets = new List<(string Name, Control Element)>();
        TopLevel TopLevelForStorage() => TopLevel.GetTopLevel(owner)
            ?? throw new InvalidOperationException("The storage controls are not attached to a top level.");

        void Add(string name, Func<Task> action)
        {
            var button = new Button { Content = name };
            button.Click += async (_, _) => await RunAsync(action, report);
            panel.Children.Add(button);
            targets.Add((name, button));
        }

        Add("Known folders", async () =>
        {
            var storage = TopLevelForStorage().StorageProvider;
            var folders = new List<string>();
            foreach (var kind in Enum.GetValues<WellKnownFolder>())
            {
                using var folder = await storage.TryGetWellKnownFolderAsync(kind);
                folders.Add($"{kind} {(folder is null ? "unavailable" : "available")}");
            }
            report(string.Join(", ", folders));
        });

        Add("Open MIME text", async () =>
        {
            var storage = TopLevelForStorage().StorageProvider;
            using var downloads = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Downloads);
            var result = await storage.OpenFilePickerWithResultAsync(new FilePickerOpenOptions
            {
                SuggestedStartLocation = downloads,
                FileTypeFilter = [MimeText, MimeXml]
            });
            using var file = result.Files.FirstOrDefault();
            if (file is null)
            {
                report("MIME picker closed");
                return;
            }
            var properties = await file.GetBasicPropertiesAsync();
            report($"MIME picker read size {properties.Size}, modified {properties.DateModified:O}, " +
                $"selected type {result.SelectedFileType?.Name ?? "unavailable"}");
        });

        Add("Save with file types", async () =>
        {
            var result = await TopLevelForStorage().StorageProvider.SaveFilePickerWithResultAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = "avalonia-storage-coverage",
                SuggestedFileType = MimeText,
                FileTypeChoices = [MimeXml, MimeText]
            });
            using var file = result.File;
            if (file is null)
            {
                report("MIME save picker closed");
                return;
            }
            await using var stream = await file.OpenWriteAsync();
            var bytes = Encoding.UTF8.GetBytes(SampleText);
            await stream.WriteAsync(bytes);
            report($"MIME save wrote {bytes.Length} bytes, selected type {result.SelectedFileType?.Name ?? "unavailable"}");
        });

        Add("Launch picked file", async () =>
        {
            var topLevel = TopLevelForStorage();
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                FileTypeFilter = [TextFiles]
            });
            using var file = files.FirstOrDefault();
            report(file is null ? "File launch picker closed"
                : await topLevel.Launcher.LaunchFileAsync(file) ? "Picked file launch accepted" : "Picked file launch unavailable");
        });

        Add("Launch picked folder", async () =>
        {
            var topLevel = TopLevelForStorage();
            if (!topLevel.StorageProvider.CanPickFolder)
            {
                report("Folder launch picker unavailable");
                return;
            }
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions());
            using var folder = folders.FirstOrDefault();
            report(folder is null ? "Folder launch picker closed"
                : await topLevel.Launcher.LaunchFileAsync(folder) ? "Picked folder launch accepted" : "Picked folder launch unavailable");
        });

        Add("Open email composer", async () =>
        {
            var opened = await TopLevelForStorage().Launcher.LaunchUriAsync(new Uri("mailto:probe@example.invalid?subject=Counter"));
            report(opened ? "Email composer launch accepted" : "Email composer launch unavailable");
        });

        Add("Open unhandled link", async () =>
        {
            var opened = await TopLevelForStorage().Launcher.LaunchUriAsync(new Uri("avalonia-counter-unhandled://probe/"));
            report(opened ? "Custom link launch accepted" : "Custom link launch unavailable");
        });

        return (panel, targets.ToArray());
    }
}
