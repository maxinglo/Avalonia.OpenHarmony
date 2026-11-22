#include <stddef.h>
#include <stdatomic.h>

#define MOCK_API __declspec(dllexport)

int _fltused;

typedef void (*ConfigCallback)(void*, void*);
typedef void (*TextCallback)(void*, const unsigned short*, size_t);
typedef void (*LengthCallback)(void*, int);
typedef void (*RangeCallback)(void*, int, int);
typedef void (*SurroundingCallback)(void*, int, unsigned short*, size_t*);
typedef int (*CaretCallback)(void*);
typedef int (*PrivateCallback)(void*, void**, size_t);
typedef int (*PreviewCallback)(void*, const unsigned short*, size_t, int, int);
typedef void (*FinishCallback)(void*);

static ConfigCallback configCallback;
static TextCallback insertCallback;
static LengthCallback deleteBackward;
static RangeCallback selectionCallback;
static SurroundingCallback leftText;
static SurroundingCallback rightText;
static CaretCallback caretIndex;
static int editor;
static int options;
static int proxy;
static PreviewCallback previewCallback;
static FinishCallback finishCallback;
static _Atomic int supportsPreview;
static int windowId;
static _Atomic int attached;
static unsigned requiredHandlers;

MOCK_API void* OH_TextEditorProxy_Create(void) { requiredHandlers = 0; return &editor; }
MOCK_API void OH_TextEditorProxy_Destroy(void* editor) {}
MOCK_API int OH_TextEditorProxy_SetGetTextConfigFunc(void* editor, ConfigCallback cb) { configCallback = cb; return 0; }
MOCK_API int OH_TextEditorProxy_SetInsertTextFunc(void* editor, TextCallback cb) { insertCallback = cb; return 0; }
MOCK_API int OH_TextEditorProxy_SetDeleteBackwardFunc(void* editor, LengthCallback cb) { deleteBackward = cb; return 0; }
MOCK_API int OH_TextEditorProxy_SetDeleteForwardFunc(void* editor, LengthCallback cb) { return 0; }
MOCK_API int OH_TextEditorProxy_SetSendEnterKeyFunc(void* editor, LengthCallback cb) { return 0; }
MOCK_API int OH_TextEditorProxy_SetHandleSetSelectionFunc(void* editor, RangeCallback cb) { selectionCallback = cb; return 0; }
MOCK_API int OH_TextEditorProxy_SetSendKeyboardStatusFunc(void* editor, LengthCallback cb) { requiredHandlers |= cb ? 1 : 0; return 0; }
MOCK_API int OH_TextEditorProxy_SetMoveCursorFunc(void* editor, LengthCallback cb) { requiredHandlers |= cb ? 2 : 0; return 0; }
MOCK_API int OH_TextEditorProxy_SetHandleExtendActionFunc(void* editor, LengthCallback cb) { requiredHandlers |= cb ? 4 : 0; return 0; }
MOCK_API int OH_TextEditorProxy_SetGetLeftTextOfCursorFunc(void* editor, SurroundingCallback cb) { leftText = cb; requiredHandlers |= cb ? 8 : 0; return 0; }
MOCK_API int OH_TextEditorProxy_SetGetRightTextOfCursorFunc(void* editor, SurroundingCallback cb) { rightText = cb; requiredHandlers |= cb ? 16 : 0; return 0; }
MOCK_API int OH_TextEditorProxy_SetGetTextIndexAtCursorFunc(void* editor, CaretCallback cb) { caretIndex = cb; requiredHandlers |= cb ? 32 : 0; return 0; }
MOCK_API int OH_TextEditorProxy_SetReceivePrivateCommandFunc(void* editor, PrivateCallback cb) { requiredHandlers |= cb ? 64 : 0; return 0; }
MOCK_API int OH_TextEditorProxy_SetSetPreviewTextFunc(void* editor, PreviewCallback cb) { previewCallback = cb; requiredHandlers |= cb ? 128 : 0; return 0; }
MOCK_API int OH_TextEditorProxy_SetFinishTextPreviewFunc(void* editor, FinishCallback cb) { finishCallback = cb; requiredHandlers |= cb ? 256 : 0; return 0; }
MOCK_API int OH_TextEditorProxy_SetCallbackInMainThread(void* editor, unsigned char mainThread) { return mainThread != 0; }
MOCK_API void* OH_AttachOptions_Create(unsigned char showKeyboard) { return showKeyboard ? &options : 0; }
MOCK_API void OH_AttachOptions_Destroy(void* options) {}

MOCK_API int OH_InputMethodController_Attach(void* editor, void* options, void** result)
{
    if(requiredHandlers != 511)
        return 12802000;
    configCallback(editor, &windowId);
    if (windowId != 17)
        return 5;
    *result = &proxy;
    atomic_store(&attached, 1);
    return 0;
}

MOCK_API int OH_InputMethodController_Detach(void* method) { atomic_store(&attached, 0); return 0; }
MOCK_API int OH_TextConfig_SetWindowId(void* config, int windowId) { *(int*)config = windowId; return 0; }
MOCK_API int OH_TextConfig_SetInputType(void* config, int type) { return 0; }
MOCK_API int OH_TextConfig_SetEnterKeyType(void* config, int type) { return 0; }
MOCK_API int OH_TextConfig_SetSelection(void* config, int start, int end) { return 0; }
MOCK_API int OH_TextConfig_SetPreviewTextSupport(void* config, unsigned char supported) { atomic_store(&supportsPreview, supported); return 0; }
MOCK_API int OH_InputMethodProxy_ShowKeyboard(void* proxy) { return 0; }
MOCK_API int OH_InputMethodProxy_NotifySelectionChange(void* proxy, const unsigned short* text, size_t length, int start, int end) { return 0; }
MOCK_API void* OH_CursorInfo_Create(double left, double top, double width, double height) { return 0; }
MOCK_API void OH_CursorInfo_Destroy(void* cursor) {}
MOCK_API int OH_InputMethodProxy_NotifyCursorUpdate(void* proxy, void* cursor) { return 0; }

MOCK_API int mock_attached(void) { return atomic_load(&attached); }
MOCK_API void mock_insert(const unsigned short* text, size_t length) { insertCallback(&editor, text, length); }
MOCK_API void mock_select(int start, int end) { selectionCallback(&editor, start, end); }
MOCK_API void mock_delete_backward(int length) { deleteBackward(&editor, length); }
MOCK_API int mock_caret_index(void) { return caretIndex(&editor); }
MOCK_API int mock_preview_supported(void) { return atomic_load(&supportsPreview); }
MOCK_API int mock_preview(const unsigned short* text, size_t length, int start, int end)
{
    return previewCallback(&editor, text, length, start, end);
}
MOCK_API void mock_finish(void) { finishCallback(&editor); }
MOCK_API size_t mock_get_surrounding(unsigned short* output, int number, int left)
{
    size_t length = (size_t)number;
    (left ? leftText : rightText)(&editor, number, output, &length);
    return length;
}
