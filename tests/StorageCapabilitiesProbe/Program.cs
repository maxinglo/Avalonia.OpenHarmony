using Avalonia.OpenHarmony;
using Avalonia.Platform.Storage;
using System.Text.Json;

const string FolderUri = "file://docs/storage/Users/currentUser/Download";
const string FileUri = FolderUri + "/%E4%B8%AD%E6%96%87.txt";
const string LocalFileUri = "file:///data/storage/el2/base/files/text.txt";
var usePreciseTime = true;
var cancelSave = false;
var requests = new List<(string Operation, string Argument)>();
var storage = new OpenHarmonyStorageProvider(RequestAsync);
var launcher = new OpenHarmonyLauncher(RequestAsync);

using var folder = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Downloads);
Check(folder?.Path.OriginalString == FolderUri && folder.Name == "Download", "The accessible directory URI was lost.");
Check(await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Documents) is null, "A denied directory was exposed.");
Check(await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Pictures) is null && requests.Count == 2,
    "An unsupported environment location requested a path.");

var text = new FilePickerFileType("Text") { MimeTypes = ["text/plain"] };
var pdf = new FilePickerFileType("PDF") { Patterns = ["*.pdf"], MimeTypes = ["application/pdf"] };
var unknown = new FilePickerFileType("Unknown") { MimeTypes = ["application/x-probe-unknown"] };
var wildcard = new FilePickerFileType("All files") { Patterns = ["*"] };
var open = await storage.OpenFilePickerWithResultAsync(new FilePickerOpenOptions
{
    AllowMultiple = true,
    SuggestedStartLocation = folder,
    FileTypeFilter = [unknown, pdf, text, wildcard]
});
using var file = open.Files.Single();
Check(file.Path.OriginalString == FileUri && file.Name == "中文.txt" && open.SelectedFileType is null,
    "Open did not preserve the original URI and unavailable selected type.");
using (var json = JsonDocument.Parse(requests.Last().Argument))
{
    Check(requests.Last().Operation == "storage.select", "Open did not activate the file picker.");
    Check(json.RootElement.GetProperty("multiple").GetBoolean() &&
        json.RootElement.GetProperty("startUri").GetString() == FolderUri && FilterText(json) == "PDF|.pdf;Text|.txt,.text;All files|.*",
        "Open did not forward the supported filter suffixes and start location.");
}

var properties = await file.GetBasicPropertiesAsync();
var expectedTime = DateTimeOffset.FromUnixTimeSeconds(1700000000);
Check(properties.Size == 24 && properties.DateModified == expectedTime.AddTicks(1234567) && properties.DateCreated is null,
    "File properties did not preserve size, precise modification time, or unknown creation time.");
usePreciseTime = false;
Check((await file.GetBasicPropertiesAsync()).DateModified == expectedTime, "The seconds timestamp was not available as a fallback.");

var options = new FilePickerSaveOptions
{
    SuggestedFileName = "coverage",
    DefaultExtension = "txt",
    FileTypeChoices = [unknown, pdf, text],
    SuggestedFileType = text
};
var save = await storage.SaveFilePickerWithResultAsync(options);
using (save.File)
{
    Check(save.File is not null && save.SelectedFileType == pdf, "The OS selection did not map to the original save type.");
    using var json = JsonDocument.Parse(requests.Last().Argument);
    Check(requests.Last().Operation == "storage.save" && FilterText(json) == "Text|.txt;PDF|.pdf" &&
        json.RootElement.GetProperty("fileName").GetString() == "coverage.txt", "Save type order or filename was incorrect.");
}
cancelSave = true;
save = await storage.SaveFilePickerWithResultAsync(options);
Check(save.File is null && save.SelectedFileType is null, "Canceling save returned a selected type or file.");

Check(await launcher.LaunchUriAsync(new Uri("https://example.invalid/")) &&
    requests.Last() == ("launcher.uri", "https://example.invalid/"), "HTTPS did not use openLink.");
Check(await launcher.LaunchUriAsync(new Uri("avalonia-probe://link/")) &&
    requests.Last() == ("launcher.uri", "avalonia-probe://link/"), "A custom deep link did not use openLink.");
Check(await launcher.LaunchUriAsync(new Uri("mailto:probe@example.invalid?subject=Test")) &&
    requests.Last() == ("launcher.mailto", "mailto:probe@example.invalid?subject=Test"), "The email composer route was lost.");
Check(await launcher.LaunchFileAsync(file) && requests.Last() == ("launcher.file", FileUri), "Launch lost the picked file URI.");
using var sandboxFile = await storage.TryGetFileFromPathAsync(new Uri(LocalFileUri));
Check(sandboxFile is not null && await launcher.LaunchFileAsync(sandboxFile) &&
    requests.Last() == ("launcher.path", "/data/storage/el2/base/files/text.txt"), "The local file did not use path conversion.");
Check(folder is not null && !await launcher.LaunchFileAsync(folder) && requests.Last() == ("launcher.folder", FolderUri),
    "An unavailable folder handler did not report failure or preserve the URI.");
var lastRequestCount = requests.Count;
Check(!await launcher.LaunchUriAsync(new Uri("relative", UriKind.Relative)) &&
    !await launcher.LaunchUriAsync(new Uri("javascript:alert(1)")) &&
    !await launcher.LaunchUriAsync(new Uri(FileUri)) && requests.Count == lastRequestCount,
    "An unsupported URI reached the system launcher.");

Console.WriteLine("Storage and launcher capability probe passed.");

Task<OpenHarmonySystemResult> RequestAsync(string operation, string argument)
{
    requests.Add((operation, argument));
    var value = operation switch
    {
        "storage.wellKnownFolder" when argument == "downloads" => $$"""{"uri":"{{FolderUri}}","name":"Download"}""",
        "storage.wellKnownFolder" when argument == "documents" => null,
        "storage.resolveMimeFilters" => ResolveMimeFilters(argument),
        "storage.select" => $$"""{"entries":[{"uri":"{{FileUri}}","name":"中文.txt"}],"selectedIndex":-1}""",
        "storage.save" when cancelSave => """{"entries":[],"selectedIndex":-1}""",
        "storage.save" => $$"""{"entries":[{"uri":"{{FileUri}}","name":"中文.txt"}],"selectedIndex":1}""",
        "storage.stat" when usePreciseTime =>
            """{"error":0,"directory":false,"size":24,"modifiedSeconds":1700000000,"modifiedNanoseconds":"1700000000123456789"}""",
        "storage.stat" => """{"error":0,"directory":false,"size":24,"modifiedSeconds":1700000000}""",
        "launcher.uri" or "launcher.mailto" or "launcher.file" or "launcher.path" => string.Empty,
        "launcher.folder" => null,
        _ => throw new InvalidOperationException($"Unexpected request {operation}.")
    };
    return Task.FromResult(new OpenHarmonySystemResult(value, null));
}

static string ResolveMimeFilters(string argument) => JsonSerializer.Serialize(JsonSerializer.Deserialize<string[][]>(argument)!
    .Select<string[], string[]>(group => group is ["text/plain"] ? [".txt", ".text", ".TXT"] : []));

static string FilterText(JsonDocument json) => string.Join(';', json.RootElement.GetProperty("filters").EnumerateArray()
    .Select(filter => filter.GetString()));

static void Check(bool condition, string error)
{
    if (!condition)
        throw new InvalidOperationException(error);
}
