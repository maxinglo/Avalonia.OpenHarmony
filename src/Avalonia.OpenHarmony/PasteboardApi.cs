using System.Runtime.InteropServices;

namespace Avalonia.OpenHarmony;

internal static unsafe partial class PasteboardApi
{
    private const string Pasteboard = "libpasteboard.so";
    private const string UnifiedData = "libudmf.so";

    [LibraryImport(Pasteboard)]
    internal static partial nint OH_Pasteboard_Create();

    [LibraryImport(Pasteboard)]
    internal static partial void OH_Pasteboard_Destroy(nint pasteboard);

    [LibraryImport(Pasteboard)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool OH_Pasteboard_HasData(nint pasteboard);

    [LibraryImport(Pasteboard)]
    internal static partial nint OH_Pasteboard_GetData(nint pasteboard, out int status);

    [LibraryImport(Pasteboard)]
    internal static partial int OH_Pasteboard_SetData(nint pasteboard, nint data);

    [LibraryImport(Pasteboard)]
    internal static partial int OH_Pasteboard_ClearData(nint pasteboard);

    [LibraryImport(UnifiedData)]
    internal static partial nint OH_UdmfData_Create();

    [LibraryImport(UnifiedData)]
    internal static partial void OH_UdmfData_Destroy(nint data);

    [LibraryImport(UnifiedData)]
    internal static partial int OH_UdmfData_AddRecord(nint data, nint record);

    [LibraryImport(UnifiedData)]
    internal static partial nint* OH_UdmfData_GetRecords(nint data, out uint count);

    [LibraryImport(UnifiedData)]
    internal static partial nint OH_UdmfRecord_Create();

    [LibraryImport(UnifiedData)]
    internal static partial void OH_UdmfRecord_Destroy(nint record);

    [LibraryImport(UnifiedData)]
    internal static partial nint* OH_UdmfRecord_GetTypes(nint record, out uint count);

    [LibraryImport(UnifiedData)]
    internal static partial int OH_UdmfRecord_AddPlainText(nint record, nint text);

    [LibraryImport(UnifiedData)]
    internal static partial int OH_UdmfRecord_GetPlainText(nint record, nint text);

    [LibraryImport(UnifiedData)]
    internal static partial int OH_UdmfRecord_AddHtml(nint record, nint html);

    [LibraryImport(UnifiedData)]
    internal static partial int OH_UdmfRecord_GetHtml(nint record, nint html);

    [LibraryImport(UnifiedData)]
    internal static partial nint OH_UdsPlainText_Create();

    [LibraryImport(UnifiedData)]
    internal static partial void OH_UdsPlainText_Destroy(nint text);

    [LibraryImport(UnifiedData, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int OH_UdsPlainText_SetContent(nint handle, string text);

    [LibraryImport(UnifiedData)]
    internal static partial nint OH_UdsPlainText_GetContent(nint handle);

    [LibraryImport(UnifiedData)]
    internal static partial nint OH_UdsHtml_Create();

    [LibraryImport(UnifiedData)]
    internal static partial void OH_UdsHtml_Destroy(nint html);

    [LibraryImport(UnifiedData, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int OH_UdsHtml_SetContent(nint handle, string html);

    [LibraryImport(UnifiedData, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int OH_UdsHtml_SetPlainContent(nint handle, string text);

    [LibraryImport(UnifiedData)]
    internal static partial nint OH_UdsHtml_GetContent(nint handle);

    [LibraryImport(UnifiedData)]
    internal static partial nint OH_UdsHtml_GetPlainContent(nint handle);
}
