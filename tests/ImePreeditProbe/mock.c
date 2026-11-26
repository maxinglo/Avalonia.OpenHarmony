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

typedef struct {
    int windowId;
    int inputType;
    int enterKey;
    int selectionStart;
    int selectionEnd;
    int supportsPreview;
    unsigned short placeholder[256];
} TextConfig;

static ConfigCallback configCallback;
static TextCallback insertCallback;
static LengthCallback deleteBackward;
static LengthCallback extendAction;
static RangeCallback selectionCallback;
static SurroundingCallback leftText;
static SurroundingCallback rightText;
static CaretCallback caretIndex;
static int editor;
static int options;
static int proxy;
static PreviewCallback previewCallback;
static FinishCallback finishCallback;
static PrivateCallback privateCallback;
static _Atomic int attached;
static _Atomic int notifiedInputType;
static _Atomic int notifiedEnterKey;
static _Atomic int notifiedSelectionLength;
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
MOCK_API int OH_TextEditorProxy_SetHandleExtendActionFunc(void* editor, LengthCallback cb) { extendAction = cb; requiredHandlers |= cb ? 4 : 0; return 0; }
MOCK_API int OH_TextEditorProxy_SetGetLeftTextOfCursorFunc(void* editor, SurroundingCallback cb) { leftText = cb; requiredHandlers |= cb ? 8 : 0; return 0; }
MOCK_API int OH_TextEditorProxy_SetGetRightTextOfCursorFunc(void* editor, SurroundingCallback cb) { rightText = cb; requiredHandlers |= cb ? 16 : 0; return 0; }
MOCK_API int OH_TextEditorProxy_SetGetTextIndexAtCursorFunc(void* editor, CaretCallback cb) { caretIndex = cb; requiredHandlers |= cb ? 32 : 0; return 0; }
MOCK_API int OH_TextEditorProxy_SetReceivePrivateCommandFunc(void* editor, PrivateCallback cb) { privateCallback = cb; requiredHandlers |= cb ? 64 : 0; return 0; }
MOCK_API int OH_TextEditorProxy_SetSetPreviewTextFunc(void* editor, PreviewCallback cb) { previewCallback = cb; requiredHandlers |= cb ? 128 : 0; return 0; }
MOCK_API int OH_TextEditorProxy_SetFinishTextPreviewFunc(void* editor, FinishCallback cb) { finishCallback = cb; requiredHandlers |= cb ? 256 : 0; return 0; }
MOCK_API int OH_TextEditorProxy_SetCallbackInMainThread(void* editor, unsigned char mainThread) { return mainThread != 0; }
MOCK_API void* OH_AttachOptions_Create(unsigned char showKeyboard) { return showKeyboard ? &options : 0; }
MOCK_API void OH_AttachOptions_Destroy(void* options) {}

MOCK_API int OH_InputMethodController_Attach(void* editor, void* options, void** result)
{
    if(requiredHandlers != 511)
        return 12802000;
    TextConfig config;
    configCallback(editor, &config);
    if (config.windowId != 17)
        return 5;
    *result = &proxy;
    atomic_store(&attached, 1);
    return 0;
}

MOCK_API int OH_InputMethodController_Detach(void* method) { atomic_store(&attached, 0); return 0; }
MOCK_API int OH_TextConfig_SetWindowId(TextConfig* config, int id) { config->windowId = id; return 0; }
MOCK_API int OH_TextConfig_SetInputType(TextConfig* config, int type) { config->inputType = type; return 0; }
MOCK_API int OH_TextConfig_SetEnterKeyType(TextConfig* config, int type) { config->enterKey = type; return 0; }
MOCK_API int OH_TextConfig_SetSelection(TextConfig* config, int start, int end)
{
    config->selectionStart = start;
    config->selectionEnd = end;
    return 0;
}
MOCK_API int OH_TextConfig_SetPreviewTextSupport(TextConfig* config, unsigned char supported) { config->supportsPreview = supported; return 0; }
MOCK_API int OH_TextConfig_SetPlaceholder(TextConfig* config, const unsigned short* text, size_t length)
{
    if (text == 0 || length == 0) {
        config->placeholder[0] = 0;
        return 0;
    }
    if (length > 256 || text[length - 1] != 0)
        return 401;
    for (size_t index = 0; index < length; index++)
        config->placeholder[index] = text[index];
    return 0;
}
MOCK_API int OH_InputMethodProxy_ShowKeyboard(void* proxy) { return 0; }
MOCK_API int OH_InputMethodProxy_NotifySelectionChange(void* proxy, const unsigned short* text, size_t length, int start, int end)
{
    atomic_store(&notifiedSelectionLength, (int)length);
    return 0;
}
MOCK_API int OH_InputMethodProxy_NotifyConfigurationChange(void* proxy, int enterKey, int inputType)
{
    atomic_store(&notifiedInputType, inputType);
    atomic_store(&notifiedEnterKey, enterKey);
    return 0;
}
MOCK_API void* OH_CursorInfo_Create(double left, double top, double width, double height) { return 0; }
MOCK_API void OH_CursorInfo_Destroy(void* cursor) {}
MOCK_API int OH_InputMethodProxy_NotifyCursorUpdate(void* proxy, void* cursor) { return 0; }

MOCK_API int mock_attached(void) { return atomic_load(&attached); }
MOCK_API void mock_insert(const unsigned short* text, size_t length) { insertCallback(&editor, text, length); }
MOCK_API void mock_select(int start, int end) { selectionCallback(&editor, start, end); }
MOCK_API void mock_delete_backward(int length) { deleteBackward(&editor, length); }
MOCK_API int mock_caret_index(void) { return caretIndex(&editor); }
MOCK_API void mock_extend_action(int action) { extendAction(&editor, action); }
MOCK_API int mock_private_command(void) { return privateCallback(&editor, 0, 0); }
MOCK_API int mock_notified_input_type(void) { return atomic_load(&notifiedInputType); }
MOCK_API int mock_notified_enter_key(void) { return atomic_load(&notifiedEnterKey); }
MOCK_API int mock_notified_selection_length(void) { return atomic_load(&notifiedSelectionLength); }
MOCK_API void mock_get_config(TextConfig* config) { configCallback(&editor, config); }
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

static char clipboard[512];
static char stagedText[512];
static int pasteboard;
static int data;
static int record;
static int plainText;
static void* records[] = { &record };
static const char* types[] = { "general.plain-text" };
static _Atomic int clipboardReadStatus;
static _Atomic int clipboardReads;
static _Atomic int clipboardWrites;

static void copy_text(char* destination, const char* source)
{
    size_t index = 0;
    while (index < sizeof(clipboard) - 1 && source[index]) {
        destination[index] = source[index];
        index++;
    }
    destination[index] = '\0';
}

MOCK_API void* OH_Pasteboard_Create(void) { return &pasteboard; }
MOCK_API void OH_Pasteboard_Destroy(void* handle) {}
MOCK_API unsigned char OH_Pasteboard_HasData(void* handle) { return clipboard[0] != '\0'; }
MOCK_API void* OH_Pasteboard_GetData(void* handle, int* status)
{
    *status = atomic_load(&clipboardReadStatus);
    atomic_fetch_add(&clipboardReads, 1);
    return *status == 0 ? &data : 0;
}
MOCK_API int OH_Pasteboard_SetData(void* handle, void* value)
{
    copy_text(clipboard, stagedText);
    atomic_fetch_add(&clipboardWrites, 1);
    return 0;
}
MOCK_API void* OH_UdmfData_Create(void) { return &data; }
MOCK_API void OH_UdmfData_Destroy(void* value) {}
MOCK_API int OH_UdmfData_AddRecord(void* value, void* item) { return 0; }
MOCK_API void** OH_UdmfData_GetRecords(void* value, unsigned* count) { *count = 1; return records; }
MOCK_API void* OH_UdmfRecord_Create(void) { return &record; }
MOCK_API void OH_UdmfRecord_Destroy(void* item) {}
MOCK_API const char** OH_UdmfRecord_GetTypes(void* item, unsigned* count) { *count = 1; return types; }
MOCK_API int OH_UdmfRecord_AddPlainText(void* item, void* text) { return 0; }
MOCK_API int OH_UdmfRecord_GetPlainText(void* item, void* text) { copy_text(stagedText, clipboard); return 0; }
MOCK_API void* OH_UdsPlainText_Create(void) { return &plainText; }
MOCK_API void OH_UdsPlainText_Destroy(void* text) {}
MOCK_API const char* OH_UdsPlainText_GetContent(void* text) { return stagedText; }
MOCK_API int OH_UdsPlainText_SetContent(void* text, const char* content) { copy_text(stagedText, content); return 0; }
MOCK_API void mock_set_clipboard_read_status(int status) { atomic_store(&clipboardReadStatus, status); }
MOCK_API int mock_clipboard_reads(void) { return atomic_load(&clipboardReads); }
MOCK_API int mock_clipboard_writes(void) { return atomic_load(&clipboardWrites); }
MOCK_API const char* mock_clipboard_text(void) { return clipboard; }
