#if OHOS
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.OpenHarmony;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Counter;

public sealed partial class CounterApp
{
    private static (Control Panel, (string Name, Control Element)[] Targets) CreateNativeControlSample()
    {
        var result = new TextBlock { Text = "ArkUI button ready" };
        var native = new CounterArkUIButton(count =>
        {
            result.Text = $"Native clicks: {count}";
            Trace(result.Text);
        }) { Width = 168, Height = 44 };
        Canvas.SetLeft(native, 8);
        Canvas.SetTop(native, 4);
        var slot = new Canvas { Children = { native } };
        var viewport = new Border
        {
            Width = 248,
            Height = 52,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderBrush = Brushes.SlateGray,
            BorderThickness = new Thickness(1),
            Child = slot
        };

        var move = new Button { Content = "Move and clip ArkUI" };
        move.Click += (_, _) =>
        {
            var x = Canvas.GetLeft(native) == 8 ? 148 : 8;
            Canvas.SetLeft(native, x);
            Trace($"Native control position requested: {x}");
        };
        var show = new Button { Content = "Toggle ArkUI visibility" };
        show.Click += (_, _) =>
        {
            native.IsVisible = !native.IsVisible;
            Trace($"Native control visible: {native.IsVisible}");
        };
        var detach = new Button { Content = "Detach or attach ArkUI" };
        detach.Click += (_, _) =>
        {
            if (!slot.Children.Remove(native))
            {
                native.IsVisible = true;
                slot.Children.Add(native);
            }
            Trace($"Native control attached: {slot.Children.Contains(native)}");
        };

        return (new StackPanel
        {
            Spacing = 8,
            Children = { new WrapPanel { Children = { move, show, detach } }, viewport, result }
        }, [("ArkUI native viewport", viewport), ("Move and clip ArkUI", move),
            ("Toggle ArkUI visibility", show), ("Detach or attach ArkUI", detach)]);
    }
}

internal sealed unsafe partial class CounterArkUIButton(Action<int> click) : NativeControlHost
{
    private GCHandle _callback;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent) =>
        OpenHarmonyNativeControlHandle.Create(parent, Create, Destroy, this);

    private nint Create(nint _)
    {
        _callback = GCHandle.Alloc(click);
        var node = CreateNativeButton(&OnClick, GCHandle.ToIntPtr(_callback));
        if (node == 0)
            _callback.Free();
        else
            CounterApp.Trace("ArkUI native button created");
        return node;
    }

    private void Destroy(nint node)
    {
        DisposeNativeButton(node);
        _callback.Free();
        CounterApp.Trace("ArkUI native button disposed");
    }

    [UnmanagedCallersOnly]
    private static void OnClick(nint context, int count)
    {
        var action = (Action<int>)GCHandle.FromIntPtr(context).Target!;
        Dispatcher.UIThread.Post(() => action(count));
    }

    [LibraryImport("libavalonia_ohos_bridge.so", EntryPoint = "avaoh_counter_create_native_button")]
    private static partial nint CreateNativeButton(delegate* unmanaged<nint, int, void> click, nint context);

    [LibraryImport("libavalonia_ohos_bridge.so", EntryPoint = "avaoh_counter_dispose_native_button")]
    private static partial void DisposeNativeButton(nint node);
}
#endif
