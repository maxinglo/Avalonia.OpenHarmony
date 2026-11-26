#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define EXPORT __declspec(dllexport)
#define STR_SIZE 256
#define ENTRY_CAPACITY 4
#define ENTRY_SIZE 4096
#define PIXEL_CAPACITY 512

typedef struct {
    uint32_t width, height;
    int32_t format, sourceFormat, alpha;
} ImageOptions;

typedef struct {
    uint32_t width, height, id;
    uint8_t rgba[PIXEL_CAPACITY];
} Image;

typedef struct {
    uint8_t* pixels;
    size_t size;
    uint32_t offset, stride, x, y, width, height;
} PixelArea;

typedef struct {
    char text[STR_SIZE], fallback[STR_SIZE];
} Text;

typedef struct {
    char type[STR_SIZE];
    uint32_t size;
    uint8_t payload[ENTRY_SIZE];
} Entry;

typedef struct {
    Text plain, html;
    Image image;
    Entry entries[ENTRY_CAPACITY];
    const char* types[ENTRY_CAPACITY + 3];
    uint32_t entryCount;
    bool hasPlain, hasHtml;
} Record;

typedef struct {
    Record records[2];
    Record* borrowed[2];
    uint32_t count;
} Transfer;

static Transfer clipboard, request;
static Record staged;
static Text plain, html;
static Image image, uds, info;
static ImageOptions options;
static uint8_t entryResult[ENTRY_SIZE];
static uint32_t imageId;
static int mode;

// Keep the fixture runnable with a freestanding DLL and no test-specific CRT discovery.
void* memcpy(void* destination, const void* source, size_t count)
{
    uint8_t* dst = destination;
    const uint8_t* src = source;
    for (size_t i = 0; i < count; ++i)
        dst[i] = src[i];
    return destination;
}

static bool copyText(char* destination, const char* source)
{
    for (size_t i = 0; i < STR_SIZE; ++i) {
        destination[i] = source[i];
        if (source[i] == '\0')
            return true;
    }
    return false;
}

static bool equalText(const char* left, const char* right)
{
    while (*left && *left == *right) {
        ++left;
        ++right;
    }
    return *left == *right;
}

EXPORT void SetProbeMode(int value) { mode = value; }
EXPORT void* OH_Pasteboard_Create(void) { return &clipboard; }
EXPORT void OH_Pasteboard_Destroy(void* handle) { (void)handle; }
EXPORT bool OH_Pasteboard_HasData(Transfer* handle) { return handle->count != 0; }
EXPORT Transfer* OH_Pasteboard_GetData(Transfer* handle, int* status)
{
    *status = mode == 2 ? 201 : 0;
    if (*status != 0)
        return NULL;
    request = *handle;
    return &request;
}
EXPORT int OH_Pasteboard_SetData(Transfer* handle, Transfer* data) { *handle = *data; return 0; }
EXPORT int OH_Pasteboard_ClearData(Transfer* handle) { handle->count = 0; return 0; }

EXPORT Transfer* OH_UdmfData_Create(void) { request.count = 0; return &request; }
EXPORT void OH_UdmfData_Destroy(Transfer* value) { value->count = 0; }
EXPORT int OH_UdmfData_AddRecord(Transfer* handle, Record* record)
{
    if (handle->count == 2)
        return 401;
    handle->records[handle->count++] = *record;
    return 0;
}
EXPORT Record** OH_UdmfData_GetRecords(Transfer* handle, uint32_t* count)
{
    *count = handle->count;
    for (uint32_t i = 0; i < handle->count; ++i)
        handle->borrowed[i] = &handle->records[i];
    return handle->borrowed;
}

EXPORT Record* OH_UdmfRecord_Create(void)
{
    staged.hasPlain = staged.hasHtml = false;
    staged.entryCount = staged.image.id = 0;
    return &staged;
}
EXPORT void OH_UdmfRecord_Destroy(Record* record) { record->entryCount = 0; }
EXPORT const char** OH_UdmfRecord_GetTypes(Record* record, uint32_t* count)
{
    *count = 0;
    if (record->hasPlain)
        record->types[(*count)++] = "general.plain-text";
    if (record->hasHtml)
        record->types[(*count)++] = "general.html";
    if (record->image.id)
        record->types[(*count)++] = "openharmony.pixel-map";
    for (uint32_t i = 0; i < record->entryCount; ++i)
        record->types[(*count)++] = record->entries[i].type;
    return record->types;
}

EXPORT Text* OH_UdsPlainText_Create(void) { plain.text[0] = '\0'; return &plain; }
EXPORT void OH_UdsPlainText_Destroy(Text* value) { value->text[0] = '\0'; }
EXPORT int OH_UdsPlainText_SetContent(Text* value, const char* text) { return copyText(value->text, text) ? 0 : 401; }
EXPORT const char* OH_UdsPlainText_GetContent(Text* value) { return value->text; }
EXPORT int OH_UdmfRecord_AddPlainText(Record* record, Text* value) { record->plain = *value; record->hasPlain = true; return 0; }
EXPORT int OH_UdmfRecord_GetPlainText(Record* record, Text* value) { *value = record->plain; return 0; }

EXPORT Text* OH_UdsHtml_Create(void) { html.text[0] = html.fallback[0] = '\0'; return &html; }
EXPORT void OH_UdsHtml_Destroy(Text* value) { value->text[0] = value->fallback[0] = '\0'; }
EXPORT int OH_UdsHtml_SetContent(Text* value, const char* text) { return copyText(value->text, text) ? 0 : 401; }
EXPORT int OH_UdsHtml_SetPlainContent(Text* value, const char* text) { return copyText(value->fallback, text) ? 0 : 401; }
EXPORT const char* OH_UdsHtml_GetContent(Text* value) { return value->text; }
EXPORT const char* OH_UdsHtml_GetPlainContent(Text* value) { return value->fallback; }
EXPORT int OH_UdmfRecord_AddHtml(Record* record, Text* value) { record->html = *value; record->hasHtml = true; return 0; }
EXPORT int OH_UdmfRecord_GetHtml(Record* record, Text* value) { *value = record->html; return 0; }

EXPORT int OH_UdmfRecord_AddGeneralEntry(Record* record, const char* type, const uint8_t* bytes, uint32_t size)
{
    if (size == 0 || size > ENTRY_SIZE || record->entryCount == ENTRY_CAPACITY)
        return 401;
    Entry* entry = &record->entries[record->entryCount++];
    if (!copyText(entry->type, type))
        return 401;
    entry->size = size;
    memcpy(entry->payload, bytes, size);
    return 0;
}

EXPORT int OH_UdmfRecord_GetGeneralEntry(Record* record, const char* type, uint8_t** bytes, uint32_t* size)
{
    for (uint32_t i = 0; i < record->entryCount; ++i) {
        Entry* entry = &record->entries[i];
        if (equalText(entry->type, type)) {
            memcpy(entryResult, entry->payload, entry->size);
            *bytes = entryResult;
            *size = entry->size;
            return 0;
        }
    }
    return 401;
}

EXPORT Image* OH_UdsPixelMap_Create(void) { uds.id = 0; return &uds; }
EXPORT void OH_UdsPixelMap_Destroy(Image* value) { value->id = 0; }
EXPORT int OH_UdsPixelMap_SetPixelMap(Image* value, Image* map) { *value = *map; return 0; }
EXPORT void OH_UdsPixelMap_GetPixelMap(Image* value, Image* map) { if (mode != 1) *map = *value; }
EXPORT int OH_UdmfRecord_AddPixelMap(Record* record, Image* value) { record->image = *value; return 0; }
EXPORT int OH_UdmfRecord_GetPixelMap(Record* record, Image* value) { *value = record->image; return 0; }

EXPORT int OH_PixelmapInitializationOptions_Create(ImageOptions** value) { *value = &options; return 0; }
EXPORT int OH_PixelmapInitializationOptions_Release(ImageOptions* value) { value->width = 0; return 0; }
EXPORT int OH_PixelmapInitializationOptions_SetWidth(ImageOptions* value, uint32_t width) { value->width = width; return 0; }
EXPORT int OH_PixelmapInitializationOptions_SetHeight(ImageOptions* value, uint32_t height) { value->height = height; return 0; }
EXPORT int OH_PixelmapInitializationOptions_SetPixelFormat(ImageOptions* value, int32_t format) { value->format = format; return 0; }
EXPORT int OH_PixelmapInitializationOptions_SetSrcPixelFormat(ImageOptions* value, int32_t format) { value->sourceFormat = format; return 0; }
EXPORT int OH_PixelmapInitializationOptions_SetAlphaType(ImageOptions* value, int32_t alpha) { value->alpha = alpha; return 0; }
EXPORT int OH_PixelmapNative_CreateEmptyPixelmap(ImageOptions* value, Image** output)
{
    image.width = value->width;
    image.height = value->height;
    image.id = ++imageId;
    *output = &image;
    return 0;
}
EXPORT int OH_PixelmapNative_CreatePixelmap(const uint8_t* pixels, size_t length, ImageOptions* value, Image** output)
{
    if (length != (size_t)value->width * value->height * 4 || length > PIXEL_CAPACITY ||
        value->format != 3 || value->sourceFormat != 3 || value->alpha != 2)
        return 401;
    OH_PixelmapNative_CreateEmptyPixelmap(value, output);
    // Like the OS constructor, consume straight-alpha RGBA and store premultiplied pixels.
    for (size_t i = 0; i < length; i += 4) {
        for (size_t c = 0; c < 3; ++c)
            image.rgba[i + c] = (uint8_t)((pixels[i + c] * pixels[i + 3] + 127) / 255);
        image.rgba[i + 3] = pixels[i + 3];
    }
    return 0;
}
EXPORT int OH_PixelmapNative_Destroy(Image** value) { (*value)->id = 0; *value = NULL; return 0; }
EXPORT int OH_PixelmapNative_GetUniqueId(Image* value, uint32_t* id) { *id = value->id; return 0; }
EXPORT int OH_PixelmapImageInfo_Create(Image** value) { *value = &info; return 0; }
EXPORT int OH_PixelmapImageInfo_Release(Image* value) { value->width = 0; return 0; }
EXPORT int OH_PixelmapNative_GetImageInfo(Image* value, Image* target) { *target = *value; return 0; }
EXPORT int OH_PixelmapImageInfo_GetWidth(Image* value, uint32_t* width) { *width = value->width; return 0; }
EXPORT int OH_PixelmapImageInfo_GetHeight(Image* value, uint32_t* height) { *height = value->height; return 0; }

EXPORT int OH_PixelmapNative_ReadPixelsFromArea(Image* value, const PixelArea* area)
{
    if (area->offset || area->x || area->y || area->width != value->width || area->height != value->height ||
        area->stride != area->width * 4 || area->size != (size_t)area->stride * area->height)
        return 401;
    for (size_t i = 0; i < area->size; i += 4) {
        unsigned alpha = value->rgba[i + 3];
        for (size_t c = 0; c < 3; ++c)
            area->pixels[i + c] = alpha ? (uint8_t)((value->rgba[i + 2 - c] * 255 + alpha / 2) / alpha) : 0;
        area->pixels[i + 3] = (uint8_t)alpha;
    }
    return 0;
}
