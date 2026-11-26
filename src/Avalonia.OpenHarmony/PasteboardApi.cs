using System.Runtime.InteropServices;

namespace Avalonia.OpenHarmony;

internal static unsafe partial class PasteboardApi
{
    private const string Pasteboard = "libpasteboard.so";
    private const string UnifiedData = "libudmf.so";
    private const string PixelMap = "libpixelmap.so";

    internal const int Rgba8888 = 3;
    internal const int PremultipliedAlpha = 2;

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
    internal static partial int OH_UdmfRecord_AddPixelMap(nint record, nint pixelMap);

    [LibraryImport(UnifiedData)]
    internal static partial int OH_UdmfRecord_GetPixelMap(nint record, nint pixelMap);

    [LibraryImport(UnifiedData, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int OH_UdmfRecord_AddGeneralEntry(nint record, string type, byte* data, uint length);

    [LibraryImport(UnifiedData, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int OH_UdmfRecord_GetGeneralEntry(nint record, string type, out byte* data, out uint length);

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

    [LibraryImport(UnifiedData)]
    internal static partial nint OH_UdsPixelMap_Create();

    [LibraryImport(UnifiedData)]
    internal static partial void OH_UdsPixelMap_Destroy(nint pixelMap);

    [LibraryImport(UnifiedData)]
    internal static partial int OH_UdsPixelMap_SetPixelMap(nint handle, nint pixelMap);

    [LibraryImport(UnifiedData)]
    internal static partial void OH_UdsPixelMap_GetPixelMap(nint handle, nint pixelMap);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapInitializationOptions_Create(out nint options);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapInitializationOptions_Release(nint options);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapInitializationOptions_SetWidth(nint options, uint width);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapInitializationOptions_SetHeight(nint options, uint height);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapInitializationOptions_SetPixelFormat(nint options, int format);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapInitializationOptions_SetSrcPixelFormat(nint options, int format);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapInitializationOptions_SetAlphaType(nint options, int alphaType);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapNative_CreatePixelmap(byte* pixels, nuint length, nint options, out nint pixelMap);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapNative_CreateEmptyPixelmap(nint options, out nint pixelMap);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapNative_Destroy(ref nint pixelMap);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapNative_GetUniqueId(nint pixelMap, out uint uniqueId);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapImageInfo_Create(out nint info);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapImageInfo_Release(nint info);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapNative_GetImageInfo(nint pixelMap, nint info);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapImageInfo_GetWidth(nint info, out uint width);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapImageInfo_GetHeight(nint info, out uint height);

    [LibraryImport(PixelMap)]
    internal static partial int OH_PixelmapNative_ReadPixelsFromArea(nint pixelMap, in PixelArea area);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct PixelArea(byte* pixels, uint width, uint height)
    {
        private readonly byte* _pixels = pixels;
        private readonly nuint _pixelsSize = (nuint)width * height * 4;
        private readonly uint _offset;
        private readonly uint _stride = width * 4;
        private readonly uint _x;
        private readonly uint _y;
        private readonly uint _width = width;
        private readonly uint _height = height;
    }
}
