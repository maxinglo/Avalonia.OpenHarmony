#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define EXPORT __declspec(dllexport)

typedef struct {
    int32_t width, height, format, sourceFormat, alpha;
} PixelOptions;

typedef struct {
    PixelOptions options;
    uint32_t pixel;
    int32_t nonZeroPixels, x, y;
} Cursor;

typedef struct {
    int32_t window, style, width, height, x, y, followSystem, format, sourceFormat, alpha, nonZeroPixels, released;
    uint32_t pixel;
} CursorSnapshot;

static PixelOptions options;
static Cursor cursors[2];
static size_t nextCursor;
static bool followSystem;
static CursorSnapshot snapshot;

EXPORT const CursorSnapshot* ReadCursorProbe(void) { return &snapshot; }

EXPORT int32_t OH_Input_SetPointerStyle(int32_t window, int32_t style)
{
    snapshot.window = window;
    snapshot.style = style;
    return 0;
}

EXPORT int32_t OH_PixelmapInitializationOptions_Create(PixelOptions** output) { *output = &options; return 0; }
EXPORT int32_t OH_PixelmapInitializationOptions_Release(PixelOptions* value) { return value != &options; }
EXPORT int32_t OH_PixelmapInitializationOptions_SetWidth(PixelOptions* value, uint32_t width)
{ value->width = (int32_t)width; return 0; }
EXPORT int32_t OH_PixelmapInitializationOptions_SetHeight(PixelOptions* value, uint32_t height)
{ value->height = (int32_t)height; return 0; }
EXPORT int32_t OH_PixelmapInitializationOptions_SetPixelFormat(PixelOptions* value, int32_t format)
{ value->format = format; return 0; }
EXPORT int32_t OH_PixelmapInitializationOptions_SetSrcPixelFormat(PixelOptions* value, int32_t format)
{ value->sourceFormat = format; return 0; }
EXPORT int32_t OH_PixelmapInitializationOptions_SetAlphaType(PixelOptions* value, int32_t alpha)
{ value->alpha = alpha; return 0; }

EXPORT int32_t OH_PixelmapNative_CreatePixelmap(const uint32_t* data, size_t length, PixelOptions* value, Cursor** output)
{
    if (nextCursor >= 2 || length != (size_t)value->width * value->height * 4)
        return 401;
    Cursor* cursor = &cursors[nextCursor++];
    cursor->options = *value;
    cursor->pixel = data[0];
    for (size_t offset = 0; offset < length / 4; ++offset) {
        if (data[offset] != 0)
            ++cursor->nonZeroPixels;
    }
    *output = cursor;
    return 0;
}

EXPORT int32_t OH_PixelmapNative_Destroy(Cursor** value)
{
    if (*value == NULL)
        return 401;
    *value = NULL;
    ++snapshot.released;
    return 0;
}

EXPORT Cursor* OH_Input_CustomCursor_Create(Cursor* value, int32_t x, int32_t y)
{ value->x = x; value->y = y; return value; }
EXPORT void OH_Input_CustomCursor_Destroy(Cursor** value) { *value = NULL; }
EXPORT bool* OH_Input_CursorConfig_Create(bool value) { followSystem = value; return &followSystem; }
EXPORT void OH_Input_CursorConfig_Destroy(bool** value) { *value = NULL; }

EXPORT int32_t OH_Input_SetCustomCursor(int32_t window, Cursor* value, const bool* config)
{
    snapshot.window = window;
    snapshot.style = -100;
    snapshot.width = value->options.width;
    snapshot.height = value->options.height;
    snapshot.x = value->x;
    snapshot.y = value->y;
    snapshot.followSystem = *config;
    snapshot.format = value->options.format;
    snapshot.sourceFormat = value->options.sourceFormat;
    snapshot.alpha = value->options.alpha;
    snapshot.nonZeroPixels = value->nonZeroPixels;
    snapshot.pixel = value->pixel;
    return 0;
}
