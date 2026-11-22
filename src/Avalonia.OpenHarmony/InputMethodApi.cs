using System.Runtime.InteropServices;

namespace Avalonia.OpenHarmony;

internal static unsafe partial class InputMethodApi
{
    private const string Library = "libohinputmethod.so";

    // Values are defined in the Native SDK's inputmethod_types_capi.h.
    internal enum EditorType
    {
        Text = 0,
        Multiline = 1,
        Number = 2,
        Email = 5,
        Url = 6,
        Password = 7,
        NumberPassword = 8,
        Decimal = 12
    }

    internal enum EnterKey
    {
        Unspecified = 0,
        Go = 2,
        Search = 3,
        Send = 4,
        Next = 5,
        Done = 6,
        Previous = 7,
        Newline = 8
    }

    internal enum CursorDirection { Up = 1, Down = 2, Left = 3, Right = 4 }
    internal enum ExtendAction { SelectAll = 0 }

    internal const int UnsupportedEditorOperation = 12800003;

    [LibraryImport(Library)] public static partial nint OH_TextEditorProxy_Create();
    [LibraryImport(Library)] public static partial void OH_TextEditorProxy_Destroy(nint editor);
    [LibraryImport(Library)] public static partial int OH_TextEditorProxy_SetGetTextConfigFunc(nint editor, delegate* unmanaged[Cdecl]<nint, nint, void> callback);
    [LibraryImport(Library)] public static partial int OH_TextEditorProxy_SetInsertTextFunc(nint editor, delegate* unmanaged[Cdecl]<nint, char*, nuint, void> callback);
    [LibraryImport(Library)] public static partial int OH_TextEditorProxy_SetDeleteBackwardFunc(nint editor, delegate* unmanaged[Cdecl]<nint, int, void> callback);
    [LibraryImport(Library)] public static partial int OH_TextEditorProxy_SetDeleteForwardFunc(nint editor, delegate* unmanaged[Cdecl]<nint, int, void> callback);
    [LibraryImport(Library)] public static partial int OH_TextEditorProxy_SetSendEnterKeyFunc(nint editor, delegate* unmanaged[Cdecl]<nint, int, void> callback);
    [LibraryImport(Library)] public static partial int OH_TextEditorProxy_SetHandleSetSelectionFunc(nint editor, delegate* unmanaged[Cdecl]<nint, int, int, void> callback);
    [LibraryImport(Library)] public static partial int OH_TextEditorProxy_SetSendKeyboardStatusFunc(nint editor, delegate* unmanaged[Cdecl]<nint, int, void> callback);
    [LibraryImport(Library)] public static partial int OH_TextEditorProxy_SetMoveCursorFunc(nint editor, delegate* unmanaged[Cdecl]<nint, CursorDirection, void> callback);
    [LibraryImport(Library)] public static partial int OH_TextEditorProxy_SetHandleExtendActionFunc(nint editor, delegate* unmanaged[Cdecl]<nint, ExtendAction, void> callback);
    [LibraryImport(Library)] public static partial int OH_TextEditorProxy_SetGetLeftTextOfCursorFunc(nint editor, delegate* unmanaged[Cdecl]<nint, int, char*, nuint*, void> callback);
    [LibraryImport(Library)] public static partial int OH_TextEditorProxy_SetGetRightTextOfCursorFunc(nint editor, delegate* unmanaged[Cdecl]<nint, int, char*, nuint*, void> callback);
    [LibraryImport(Library)] public static partial int OH_TextEditorProxy_SetGetTextIndexAtCursorFunc(nint editor, delegate* unmanaged[Cdecl]<nint, int> callback);
    [LibraryImport(Library)] public static partial int OH_TextEditorProxy_SetReceivePrivateCommandFunc(nint editor, delegate* unmanaged[Cdecl]<nint, nint*, nuint, int> callback);
    [LibraryImport(Library)] public static partial int OH_TextEditorProxy_SetSetPreviewTextFunc(nint editor, delegate* unmanaged[Cdecl]<nint, char*, nuint, int, int, int> callback);
    [LibraryImport(Library)] public static partial int OH_TextEditorProxy_SetFinishTextPreviewFunc(nint editor, delegate* unmanaged[Cdecl]<nint, void> callback);
    [LibraryImport(Library)] public static partial int OH_TextEditorProxy_SetCallbackInMainThread(nint editor, byte mainThread);
    [LibraryImport(Library)] public static partial nint OH_AttachOptions_Create(byte showKeyboard);
    [LibraryImport(Library)] public static partial void OH_AttachOptions_Destroy(nint options);
    [LibraryImport(Library)] public static partial int OH_InputMethodController_Attach(nint editor, nint options, out nint proxy);
    [LibraryImport(Library)] public static partial int OH_InputMethodController_Detach(nint proxy);
    [LibraryImport(Library)] public static partial int OH_TextConfig_SetWindowId(nint config, int windowId);
    [LibraryImport(Library)] public static partial int OH_TextConfig_SetInputType(nint config, EditorType type);
    [LibraryImport(Library)] public static partial int OH_TextConfig_SetEnterKeyType(nint config, EnterKey type);
    [LibraryImport(Library)] public static partial int OH_TextConfig_SetSelection(nint config, int start, int end);
    [LibraryImport(Library)] public static partial int OH_TextConfig_SetPreviewTextSupport(nint config, byte supported);
    [LibraryImport(Library)] public static partial int OH_InputMethodProxy_ShowKeyboard(nint proxy);
    [LibraryImport(Library)] public static partial int OH_InputMethodProxy_NotifySelectionChange(nint proxy, char* text, nuint length, int start, int end);
    [LibraryImport(Library)] public static partial nint OH_CursorInfo_Create(double left, double top, double width, double height);
    [LibraryImport(Library)] public static partial void OH_CursorInfo_Destroy(nint cursor);
    [LibraryImport(Library)] public static partial int OH_InputMethodProxy_NotifyCursorUpdate(nint proxy, nint cursor);
}
