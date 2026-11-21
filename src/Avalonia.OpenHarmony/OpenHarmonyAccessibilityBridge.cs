using System.Runtime.InteropServices;

namespace Avalonia.OpenHarmony;

internal static unsafe partial class OpenHarmonyAccessibilityBridge
{
    public static void Publish(OpenHarmonyAccessibilitySnapshot snapshot)
    {
        var nodes = new NativeNode[snapshot.Nodes.Count];
        try
        {
            for (int index = 0; index < nodes.Length; index++)
            {
                var source = snapshot.Nodes[index];
                ref var node = ref nodes[index];
                node.Id = source.Id;
                node.Parent = source.ParentId;
                node.State = source.State;
                node.Left = source.Bounds.X;
                node.Top = source.Bounds.Y;
                node.Right = source.Bounds.Right;
                node.Bottom = source.Bounds.Bottom;
                node.Role = Marshal.StringToCoTaskMemUTF8(source.Role);
                node.Name = Marshal.StringToCoTaskMemUTF8(source.Name);
            }
            fixed (NativeNode* pointer = nodes)
                Submit(snapshot.Surface, snapshot.Registration, pointer, nodes.Length);
        }
        finally
        {
            foreach (var node in nodes)
            {
                Marshal.FreeCoTaskMem(node.Role);
                Marshal.FreeCoTaskMem(node.Name);
            }
        }
    }

    [LibraryImport("libavalonia_ohos_bridge.so", EntryPoint = "avaoh_submit_accessibility")]
    private static partial void Submit(long surface, long registration, NativeNode* nodes, int count);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeNode
    {
        public int Id;
        public int Parent;
        public OpenHarmonyAccessibilityState State;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public nint Role;
        public nint Name;
    }
}
