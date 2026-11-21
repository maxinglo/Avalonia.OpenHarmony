using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using System.ComponentModel;
using Avalonia.Input.TextInput;
using Avalonia.OpenHarmony;
using Avalonia.Themes.Simple;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Runtime.InteropServices;

var dll = NativeLibrary.Load(Path.GetFullPath(args[0]));
NativeLibrary.SetDllImportResolver(typeof(OpenHarmonyHost).Assembly, (name, _, _) =>
    name == "libohinputmethod.so" ? dll : IntPtr.Zero);
var mock = new MockInput(dll);
var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var host = new OpenHarmonyHost(AppBuilder.Configure<ProbeApp>,
    (_, _, _, _, _) => firstFrame.TrySetResult(), Console.Error.WriteLine, () => 17);

host.UpdateSurface(1, 480, 300, 1);
await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
host.Touch(1, 0, TouchPhase.Begin, 35, 30);
host.Touch(1, 0, TouchPhase.End, 35, 30);
await WaitUntil(() => Task.FromResult(mock.Attached == 1));
Console.WriteLine("TextBox touch focus attached input with window ID 17");
mock.Select(9, 9);
mock.Insert("中🎧");
await WaitUntil(() => Dispatcher.UIThread.InvokeAsync(() => ProbeApp.Field!.Text == "127.0.0.1中🎧").GetTask());
Console.WriteLine("UTF-16 insertion passed");
mock.DeleteBackward(2);
await WaitUntil(() => Dispatcher.UIThread.InvokeAsync(() => ProbeApp.Field!.Text == "127.0.0.1中").GetTask());
Console.WriteLine("UTF-16 backspace passed");
mock.Select(0, 3);
mock.Insert("192");
await WaitUntil(() => Dispatcher.UIThread.InvokeAsync(() => ProbeApp.Field!.Text == "192.0.0.1中").GetTask());
Console.WriteLine("Selection replacement passed");
mock.Select(4, 4);
await WaitUntil(() => Task.FromResult(mock.Caret == 4));
if (mock.GetSurrounding(left: true, 4) != "192." || mock.GetSurrounding(left: false, 4) != "0.0.")
    throw new InvalidOperationException("Unexpected surrounding text.");
Console.WriteLine("Caret index and surrounding text passed");

await Ui(() =>
{
    ProbeApp.Field!.Text = "abcd";
    ProbeApp.Field.SelectAll();
    ProbeApp.Field.SelectionStart = 1;
    ProbeApp.Field.SelectionEnd = 3;
});
Equal(1, mock.PreviewSupported);
Equal(401, mock.Preview("outside", 0, 1));
Equal(0, mock.Preview("拼", -1, -1));
await Ui(() =>
{
    Equal("abcd", ProbeApp.Field!.Text);
    Equal(1, ProbeApp.Field.SelectionStart);
    Equal(3, ProbeApp.Field.SelectionEnd);
    Equal("拼", GetPresenter(ProbeApp.Field).PreeditText);
    Equal("a拼d", DisplayText(ProbeApp.Field));
});
Equal(0, mock.Preview("拼音", -1, -1));
Equal(0, mock.Preview("写", 2, 3));
Equal(0, mock.Preview("好", 2, 2));
Equal(401, mock.Preview("out", 0, 1));
Equal(401, mock.Preview("bad", -1, 3));
Equal("a拼好", mock.GetSurrounding(left: true, 5));
Equal("写d", mock.GetSurrounding(left: false, 5));
await Ui(() => Equal("a拼好写d", DisplayText(ProbeApp.Field!)));
mock.Finish();
mock.Finish();
await Ui(() =>
{
    Equal("a拼好写d", ProbeApp.Field!.Text);
    Equal(null, GetPresenter(ProbeApp.Field).PreeditText);
    Equal(4, ProbeApp.Field.CaretIndex);
    ProbeApp.Field.Undo();
    Equal("abcd", ProbeApp.Field.Text);
    ProbeApp.Field.Redo();
    Equal("a拼好写d", ProbeApp.Field.Text);
});
Console.WriteLine("Full preview, absolute subrange/insert, bounds, native context and single Finish commit with undo and redo passed");

await Ui(() =>
{
    ProbeApp.Field!.Text = "z💡X";
    ProbeApp.Field.SelectionStart = 1;
    ProbeApp.Field.SelectionEnd = 3;
});
Equal(0, mock.Preview("候选", -1, -1));
mock.Insert("确定");
mock.Finish();
await Ui(() => Equal("z确定X", ProbeApp.Field!.Text));
Console.WriteLine("InsertText replaces pending preview before Finish without duplicate commit");

await Ui(() =>
{
    ProbeApp.Field!.Text = "start";
    ProbeApp.Field.CaretIndex = 2;
    ProbeApp.Field.SelectionStart = 2;
    ProbeApp.Field.SelectionEnd = 2;
});
Equal(0, mock.Preview("临时", -1, -1));
Equal(0, mock.Preview("", -1, -1));
mock.Finish();
await Ui(() => Equal("start", ProbeApp.Field!.Text));
Equal(0, mock.Preview("替换", 1, 4));
await Ui(() =>
{
    Equal("s替换t", DisplayText(ProbeApp.Field!));
    var impl = TopLevel.GetTopLevel(ProbeApp.Field!)!.PlatformImpl!;
    ((ITextInputMethodImpl)impl.TryGetFeature(typeof(ITextInputMethodImpl))!).Reset();
    Equal("start", ProbeApp.Field!.Text);
    Equal("start", DisplayText(ProbeApp.Field));
    Equal(2, ProbeApp.Field.CaretIndex);
});
mock.Finish();
await Ui(() => Equal("start", ProbeApp.Field!.Text));
Console.WriteLine("Empty preview cancels insertion and editor Reset restores selected/replaced backing text");

var model = new ProbeModel("first");
await Ui(() =>
{
    ProbeApp.Field!.Bind(TextBox.TextProperty, new Binding(nameof(ProbeModel.Text)) { Source = model, Mode = BindingMode.TwoWay });
    ProbeApp.Field!.SelectionStart = 1;
    ProbeApp.Field.SelectionEnd = 3;
});
Equal(0, mock.Preview("甲", -1, -1));
await Ui(() =>
{
    Equal("first", model.Text);
    Equal("f甲st", DisplayText(ProbeApp.Field!));
    model.Text = "external";
    Equal("external", model.Text);
    Equal("external", ProbeApp.Field!.Text);
    Equal("external", DisplayText(ProbeApp.Field));
});
mock.Finish();
await Ui(() =>
{
    Equal("external", model.Text);
    ProbeApp.Field!.SelectionStart = 2;
    ProbeApp.Field!.SelectionEnd = 2;
});
Equal(0, mock.Preview("文💡段", -1, -1));
Equal(401, mock.Preview("X", 4, 5));
Equal(401, mock.Preview("X", 4, 4));
Equal(0, mock.Preview("字", 5, 6));
mock.DeleteBackward(1);
await Ui(() =>
{
    Equal("external", model.Text);
    Equal("ex文💡ternal", DisplayText(ProbeApp.Field!));
});
mock.DeleteBackward(1);
await Ui(() => Equal("ex文ternal", DisplayText(ProbeApp.Field!)));
mock.Finish();
await Ui(() => Equal("ex文ternal", model.Text));
Console.WriteLine("Two-way model survives provisional text and external update, then receives only committed text");
Console.WriteLine("Preview operations reject split UTF-16 surrogates and backspace removes the whole pair");

await Ui(() => ProbeApp.Password!.Focus());
await Ui(() =>
{
    Equal(0, mock.PreviewSupported);
    Equal(12800011, mock.Preview("禁止", -1, -1));
    Equal("", mock.GetSurrounding(left: true, 20));
    Equal(null, GetPresenter(ProbeApp.Password!).PreeditText);
});
Console.WriteLine("PasswordChar disables SDK preedit support, rejects preview and hides surrounding data");

host.DestroySurface(1);
await WaitUntil(() => Task.FromResult(mock.Attached == 0));
Console.WriteLine("Surface destruction detached the input method");

static async Task WaitUntil(Func<Task<bool>> predicate)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!await predicate())
        await Task.Delay(20, deadline.Token);
}

static Task Ui(Action action) => Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Input).GetTask();

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

static TextPresenter GetPresenter(TextBox field) =>
    field.GetVisualDescendants().OfType<TextPresenter>().Single();

static string DisplayText(TextBox field) =>
    string.Concat(GetPresenter(field).TextLayout.TextLines.SelectMany(line => line.TextRuns).Select(run => run.Text.ToString()));

public sealed class ProbeApp : Application
{
    public static TextBox? Field { get; private set; }
    public static TextBox? Password { get; private set; }

    public override void Initialize() => Styles.Add(new SimpleTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        Field = new TextBox { Text = "127.0.0.1", Height = 50 };
        Password = new TextBox { Text = "secret", PasswordChar = '*', Height = 50 };
        if (ApplicationLifetime is ISingleViewApplicationLifetime lifetime)
            lifetime.MainView = new StackPanel { Children = { Field, Password } };
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class ProbeModel(string text) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Text
    {
        get => text;
        set
        {
            if (this.Text == value)
                return;
            text = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }
}

internal unsafe sealed class MockInput(nint library)
{
    private readonly delegate* unmanaged[Cdecl]<int> _attached =
        (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(library, "mock_attached");
    private readonly delegate* unmanaged[Cdecl]<char*, nuint, void> _insert =
        (delegate* unmanaged[Cdecl]<char*, nuint, void>)NativeLibrary.GetExport(library, "mock_insert");
    private readonly delegate* unmanaged[Cdecl]<int, int, void> _select =
        (delegate* unmanaged[Cdecl]<int, int, void>)NativeLibrary.GetExport(library, "mock_select");
    private readonly delegate* unmanaged[Cdecl]<int, void> _delete =
        (delegate* unmanaged[Cdecl]<int, void>)NativeLibrary.GetExport(library, "mock_delete_backward");
    private readonly delegate* unmanaged[Cdecl]<int> _caret =
        (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(library, "mock_caret_index");
    private readonly delegate* unmanaged[Cdecl]<char*, int, int, nuint> _surrounding =
        (delegate* unmanaged[Cdecl]<char*, int, int, nuint>)NativeLibrary.GetExport(library, "mock_get_surrounding");
    private readonly delegate* unmanaged[Cdecl]<int> _previewSupported =
        (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(library, "mock_preview_supported");
    private readonly delegate* unmanaged[Cdecl]<char*, nuint, int, int, int> _preview =
        (delegate* unmanaged[Cdecl]<char*, nuint, int, int, int>)NativeLibrary.GetExport(library, "mock_preview");
    private readonly delegate* unmanaged[Cdecl]<void> _finish =
        (delegate* unmanaged[Cdecl]<void>)NativeLibrary.GetExport(library, "mock_finish");

    public int Attached => _attached();
    public int Caret => _caret();
    public int PreviewSupported => _previewSupported();
    public void Select(int start, int end) => _select(start, end);
    public void DeleteBackward(int length) => _delete(length);
    public void Finish() => _finish();

    public int Preview(string value, int start, int end)
    {
        fixed (char* text = value)
            return _preview(text, (nuint)value.Length, start, end);
    }

    public void Insert(string value)
    {
        fixed (char* text = value)
            _insert(text, (nuint)value.Length);
    }

    public string GetSurrounding(bool left, int units)
    {
        var buffer = stackalloc char[units];
        var length = _surrounding(buffer, units, left ? 1 : 0);
        return new string(buffer, 0, checked((int)length));
    }
}
