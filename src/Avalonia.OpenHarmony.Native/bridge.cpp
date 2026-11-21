#include <atomic>
#include <charconv>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <dlfcn.h>
#include <mutex>
#include <poll.h>
#include <string>
#include <unistd.h>
#include <hilog/log.h>
#include <napi/native_api.h>
#include <native_buffer/native_buffer.h>
#include <native_window/external_window.h>
#include "system_requests.h"
#include "platform_displays.h"

#include "accessibility.h"

namespace {
constexpr char Tag[] = "AvaloniaOHOS";
constexpr unsigned LogDomain = 0x0202;

struct AppLibrary {
    AppLibrary()
    {
        handle = dlopen(AVALONIA_OHOS_APP_LIBRARY, RTLD_NOW | RTLD_LOCAL);
        if (handle == nullptr) {
            OH_LOG_Print(LOG_APP, LOG_ERROR, LogDomain, Tag, "dlopen: %{public}s", dlerror());
            return;
        }
        resize = reinterpret_cast<decltype(resize)>(dlsym(handle, "avaoh_resize"));
        destroy = reinterpret_cast<decltype(destroy)>(dlsym(handle, "avaoh_destroy"));
        touch = reinterpret_cast<decltype(touch)>(dlsym(handle, "avaoh_touch"));
        pointer = reinterpret_cast<decltype(pointer)>(dlsym(handle, "avaoh_pointer"));
        key = reinterpret_cast<decltype(key)>(dlsym(handle, "avaoh_key"));
        inputBlur = reinterpret_cast<decltype(inputBlur)>(dlsym(handle, "avaoh_input_blur"));
        setLifecycle = reinterpret_cast<decltype(setLifecycle)>(dlsym(handle, "avaoh_set_lifecycle"));
        setTheme = reinterpret_cast<decltype(setTheme)>(dlsym(handle, "avaoh_set_theme"));
        setDisplays = reinterpret_cast<decltype(setDisplays)>(dlsym(handle, "avaoh_set_displays"));
        setWindowPosition = reinterpret_cast<decltype(setWindowPosition)>(dlsym(handle, "avaoh_set_window_position"));
        setSurfaceOffset = reinterpret_cast<decltype(setSurfaceOffset)>(dlsym(handle, "avaoh_set_surface_offset"));
        setAvoidArea = reinterpret_cast<decltype(setAvoidArea)>(dlsym(handle, "avaoh_set_avoid_area"));
        setWindowInsetProperties = reinterpret_cast<decltype(setWindowInsetProperties)>(
            dlsym(handle, "avaoh_set_window_inset_properties"));
        clearWindowInsets = reinterpret_cast<decltype(clearWindowInsets)>(dlsym(handle, "avaoh_clear_window_insets"));
        requestBack = reinterpret_cast<decltype(requestBack)>(dlsym(handle, "avaoh_request_back"));
        paste = reinterpret_cast<decltype(paste)>(dlsym(handle, "avaoh_paste"));
        accessibilityConfigure = reinterpret_cast<decltype(accessibilityConfigure)>(dlsym(handle, "avaoh_accessibility_configure"));
        accessibilityInvoke = reinterpret_cast<decltype(accessibilityInvoke)>(dlsym(handle, "avaoh_accessibility_invoke"));
    }

    // Keep the NativeAOT library loaded for the process lifetime.
    void* handle = nullptr;
    void (*resize)(int64_t, int32_t, int32_t, double) = nullptr;
    void (*destroy)(int64_t) = nullptr;
    void (*touch)(int64_t, int64_t, int32_t, double, double) = nullptr;
    void (*pointer)(int64_t, int32_t, int32_t, double, double, double, double, int32_t) = nullptr;
    int32_t (*key)(int64_t, int32_t, int32_t, int32_t, int32_t, int32_t) = nullptr;
    void (*inputBlur)(int64_t) = nullptr;
    void (*setLifecycle)(int32_t) = nullptr;
    void (*setTheme)(int32_t) = nullptr;
    avalonia::DisplayCallback setDisplays = nullptr;
    void (*setWindowPosition)(int64_t, int32_t, int32_t) = nullptr;
    void (*setSurfaceOffset)(int64_t, int32_t, int32_t) = nullptr;
    void (*setAvoidArea)(int32_t, const int32_t*) = nullptr;
    void (*setWindowInsetProperties)(int32_t, int32_t, uint32_t) = nullptr;
    void (*clearWindowInsets)() = nullptr;
    int32_t (*requestBack)() = nullptr;
    void (*paste)() = nullptr;
    accessibility::Configure accessibilityConfigure = nullptr;
    accessibility::Invoke accessibilityInvoke = nullptr;

    bool ready() const { return resize != nullptr && destroy != nullptr && touch != nullptr; }
};

AppLibrary& App()
{
    static AppLibrary app;
    return app;
}

struct Surface {
    OHNativeWindow* window = nullptr;
    uint64_t id = 0;
    int64_t token = 0;
    int32_t width = 0;
    int32_t height = 0;
    bool reported = false;
    bool warned = false;
    bool eglUsage = false;
    unsigned eglReferences = 0;
};

std::mutex surfaceMutex;
Surface surface;
int64_t nextToken = 0;
std::atomic<int32_t> inputWindowId{0};

void ResetSurface()
{
    if (surface.window != nullptr)
        OH_NativeWindow_DestroyNativeWindow(surface.window);
    surface = {};
}

bool ParseSurfaceId(napi_env env, napi_value value, uint64_t& surfaceId)
{
    char text[24];
    size_t length = 0;
    if (napi_get_value_string_utf8(env, value, text, sizeof(text), &length) != napi_ok)
        return false;
    const auto parsed = std::from_chars(text, text + length, surfaceId);
    return parsed.ec == std::errc{} && parsed.ptr == text + length && surfaceId != 0;
}

int64_t SurfaceToken(uint64_t surfaceId)
{
    std::lock_guard lock(surfaceMutex);
    return surface.id == surfaceId ? surface.token : 0;
}

napi_value Error(napi_env env, const char* message)
{
    napi_throw_error(env, nullptr, message);
    return nullptr;
}

napi_value ConfigureFiles(napi_env env, napi_callback_info info)
{
    size_t count = 1;
    napi_value argument;
    char filesDirectory[4096];
    size_t length = 0;
    if (napi_get_cb_info(env, info, &count, &argument, nullptr, nullptr) != napi_ok || count != 1 ||
        napi_get_value_string_utf8(env, argument, filesDirectory, sizeof(filesDirectory), &length) != napi_ok ||
        length == 0 || length == sizeof(filesDirectory) - 1 || filesDirectory[0] != '/' ||
        std::strlen(filesDirectory) != length)
        return Error(env, "Provide the application files directory from UIAbilityContext.");

    const auto configuration = std::string(filesDirectory) + "/avalonia-fonts.conf";
    FILE* file = std::fopen(configuration.c_str(), "w");
    if (file == nullptr)
        return Error(env, "Cannot open the Avalonia font configuration.");
    const auto result = std::fputs(
        "<?xml version=\"1.0\"?>\n<!DOCTYPE fontconfig SYSTEM \"urn:fontconfig:fonts.dtd\">\n"
        "<fontconfig><dir>/system/fonts</dir><cachedir prefix=\"xdg\">fontconfig</cachedir></fontconfig>\n",
        file);
    const auto closed = std::fclose(file);
    if (result < 0 || closed != 0 || setenv("XDG_CACHE_HOME", filesDirectory, 0) != 0 ||
        setenv("FONTCONFIG_FILE", configuration.c_str(), 0) != 0)
        return Error(env, "Cannot configure the Avalonia system fonts.");
    return nullptr;
}

napi_value ConfigureInputMethod(napi_env env, napi_callback_info info)
{
    size_t count = 1;
    napi_value argument;
    int32_t windowId;
    if (napi_get_cb_info(env, info, &count, &argument, nullptr, nullptr) != napi_ok || count != 1 ||
        napi_get_value_int32(env, argument, &windowId) != napi_ok || windowId <= 0)
        return Error(env, "Provide a valid HarmonyOS window ID for the input method.");
    inputWindowId.store(windowId, std::memory_order_relaxed);
    return nullptr;
}

napi_value SetLifecycle(napi_env env, napi_callback_info info)
{
    size_t count = 1;
    napi_value argument;
    int32_t transition;
    if (napi_get_cb_info(env, info, &count, &argument, nullptr, nullptr) != napi_ok || count != 1 ||
        napi_get_value_int32(env, argument, &transition) != napi_ok || transition < 0 || transition > 5)
        return Error(env, "Invalid Avalonia lifecycle transition.");
    if (auto callback = App().setLifecycle; callback != nullptr)
        callback(transition);
    return nullptr;
}

napi_value SetTheme(napi_env env, napi_callback_info info)
{
    size_t count = 1;
    napi_value argument;
    bool dark;
    if (napi_get_cb_info(env, info, &count, &argument, nullptr, nullptr) != napi_ok || count != 1 ||
        napi_get_value_bool(env, argument, &dark) != napi_ok)
        return Error(env, "Expected a platform dark-mode flag.");
    if (auto callback = App().setTheme; callback != nullptr)
        callback(dark ? 1 : 0);
    return nullptr;
}

napi_value RefreshDisplays(napi_env, napi_callback_info)
{
    if (auto callback = App().setDisplays; callback != nullptr)
        avalonia::PublishDisplays(callback);
    return nullptr;
}

napi_value SetWindowPosition(napi_env env, napi_callback_info info)
{
    size_t count = 3;
    napi_value args[3];
    int64_t displayId;
    int32_t x, y;
    if (napi_get_cb_info(env, info, &count, args, nullptr, nullptr) != napi_ok || count != 3 ||
        napi_get_value_int64(env, args[0], &displayId) != napi_ok || displayId < 0 ||
        napi_get_value_int32(env, args[1], &x) != napi_ok || napi_get_value_int32(env, args[2], &y) != napi_ok)
        return Error(env, "Expected a display ID and pixel coordinates.");
    if (auto callback = App().setWindowPosition; callback != nullptr)
        callback(displayId, x, y);
    return nullptr;
}

napi_value SetSurfaceOffset(napi_env env, napi_callback_info info)
{
    size_t count = 3;
    napi_value args[3];
    uint64_t surfaceId;
    int32_t x, y;
    if (napi_get_cb_info(env, info, &count, args, nullptr, nullptr) != napi_ok || count != 3 ||
        !ParseSurfaceId(env, args[0], surfaceId) ||
        napi_get_value_int32(env, args[1], &x) != napi_ok || napi_get_value_int32(env, args[2], &y) != napi_ok)
        return Error(env, "Expected an XComponent surface ID and pixel coordinates.");

    int64_t token = 0;
    {
        std::lock_guard lock(surfaceMutex);
        if (surface.id == surfaceId)
            token = surface.token;
    }
    if (auto callback = App().setSurfaceOffset; token != 0 && callback != nullptr)
        callback(token, x, y);
    return nullptr;
}

napi_value RequestBack(napi_env env, napi_callback_info)
{
    auto callback = App().requestBack;
    napi_value result = nullptr;
    napi_get_boolean(env, callback != nullptr && callback() != 0, &result);
    return result;
}

napi_value SetAvoidArea(napi_env env, napi_callback_info info)
{
    size_t count = 2;
    napi_value args[2];
    int32_t kind;
    bool isArray = false;
    uint32_t length = 0;
    int32_t rects[16];
    if (napi_get_cb_info(env, info, &count, args, nullptr, nullptr) != napi_ok || count != 2 ||
        napi_get_value_int32(env, args[0], &kind) != napi_ok || kind < 0 || kind == 2 || kind > 5 ||
        napi_is_array(env, args[1], &isArray) != napi_ok || !isArray ||
        napi_get_array_length(env, args[1], &length) != napi_ok || length != 16) {
        OH_LOG_Print(LOG_APP, LOG_WARN, LogDomain, Tag, "Ignored a malformed window avoidance area");
        return nullptr;
    }
    for (uint32_t i = 0; i < length; ++i) {
        napi_value element;
        if (napi_get_element(env, args[1], i, &element) != napi_ok ||
            napi_get_value_int32(env, element, &rects[i]) != napi_ok) {
            OH_LOG_Print(LOG_APP, LOG_WARN, LogDomain, Tag, "Ignored a malformed window avoidance coordinate");
            return nullptr;
        }
    }
    if (auto callback = App().setAvoidArea; callback != nullptr)
        callback(kind, rects);
    return nullptr;
}

napi_value SetWindowInsetProperties(napi_env env, napi_callback_info info)
{
    size_t count = 3;
    napi_value args[3];
    bool edgeToEdge, hasColor;
    uint32_t color;
    if (napi_get_cb_info(env, info, &count, args, nullptr, nullptr) != napi_ok || count != 3 ||
        napi_get_value_bool(env, args[0], &edgeToEdge) != napi_ok ||
        napi_get_value_bool(env, args[1], &hasColor) != napi_ok ||
        napi_get_value_uint32(env, args[2], &color) != napi_ok)
        return Error(env, "Expected immersive mode and the optional ARGB status bar color.");
    if (auto callback = App().setWindowInsetProperties; callback != nullptr)
        callback(edgeToEdge ? 1 : 0, hasColor ? 1 : 0, color);
    return nullptr;
}

napi_value ClearWindowInsets(napi_env, napi_callback_info)
{
    if (auto callback = App().clearWindowInsets; callback != nullptr)
        callback();
    return nullptr;
}

napi_value UpdateSurface(napi_env env, napi_callback_info info)
{
    size_t count = 4;
    napi_value args[4];
    uint64_t surfaceId;
    int32_t width, height;
    double scaling;
    if (napi_get_cb_info(env, info, &count, args, nullptr, nullptr) != napi_ok || count != 4 ||
        !ParseSurfaceId(env, args[0], surfaceId) ||
        napi_get_value_int32(env, args[1], &width) != napi_ok ||
        napi_get_value_int32(env, args[2], &height) != napi_ok ||
        napi_get_value_double(env, args[3], &scaling) != napi_ok ||
        width <= 0 || height <= 0 || static_cast<int64_t>(width) * height > 16'777'216 ||
        !std::isfinite(scaling) || scaling <= 0)
        return Error(env, "Invalid XComponent surface.");

    auto& app = App();
    if (!app.ready())
        return Error(env, "The Avalonia NativeAOT library or its exports could not be loaded.");

    int64_t oldToken = 0;
    int64_t token = 0;
    int status = 0;
    {
        std::lock_guard lock(surfaceMutex);
        if (surface.id != surfaceId) {
            oldToken = surface.token;
            ResetSurface();
            status = OH_NativeWindow_CreateNativeWindowFromSurfaceId(surfaceId, &surface.window);
            if (status == 0) {
                surface.id = surfaceId;
                surface.token = ++nextToken;
                status = OH_NativeWindow_NativeWindowHandleOpt(surface.window, SET_FORMAT,
                    NATIVEBUFFER_PIXEL_FMT_RGBA_8888);
                if (status == 0)
                    status = OH_NativeWindow_NativeWindowHandleOpt(surface.window, SET_USAGE,
                        static_cast<uint64_t>(NATIVEBUFFER_USAGE_CPU_WRITE | NATIVEBUFFER_USAGE_MEM_DMA));
            }
            if (status != 0)
                ResetSurface();
        }

        if (status == 0 && surface.window != nullptr) {
            status = OH_NativeWindow_NativeWindowHandleOpt(surface.window, SET_BUFFER_GEOMETRY, width, height);
            if (status == 0) {
                surface.width = width;
                surface.height = height;
                token = surface.token;
            }
        }
    }
    if (oldToken != 0) {
        accessibility::Detach(oldToken);
        app.destroy(oldToken);
    }
    if (token == 0) {
        OH_LOG_Print(LOG_APP, LOG_ERROR, LogDomain, Tag, "NativeWindow setup failed: %{public}d", status);
        return Error(env, "Could not configure the XComponent native window.");
    }
    app.resize(token, width, height, scaling);
    return nullptr;
}

napi_value CloseSurface(napi_env env, napi_callback_info info)
{
    size_t count = 1;
    napi_value argument;
    uint64_t surfaceId;
    if (napi_get_cb_info(env, info, &count, &argument, nullptr, nullptr) != napi_ok ||
        count != 1 || !ParseSurfaceId(env, argument, surfaceId))
        return Error(env, "Invalid XComponent surface ID.");
    int64_t token = 0;
    {
        std::lock_guard lock(surfaceMutex);
        if (surface.id == surfaceId) {
            token = surface.token;
            ResetSurface();
        }
    }
    if (token != 0 && App().ready()) {
        accessibility::Detach(token);
        App().destroy(token);
    }
    return nullptr;
}

napi_value ConfigureAccessibility(napi_env env, napi_callback_info info)
{
    size_t count = 5;
    napi_value args[5];
    uint64_t surfaceId;
    bool enabled;
    int32_t screenX, screenY;
    if (napi_get_cb_info(env, info, &count, args, nullptr, nullptr) != napi_ok || count != 5 ||
        !ParseSurfaceId(env, args[0], surfaceId) || napi_get_value_bool(env, args[1], &enabled) != napi_ok ||
        napi_get_value_int32(env, args[3], &screenX) != napi_ok || napi_get_value_int32(env, args[4], &screenY) != napi_ok)
        return Error(env, "Invalid accessibility surface or state.");
    int64_t token;
    {
        std::lock_guard lock(surfaceMutex);
        token = surface.id == surfaceId ? surface.token : 0;
    }
    int32_t result = -1;
    if (token != 0) {
        if (!enabled) {
            accessibility::Detach(token);
            result = 0;
        } else {
            auto& app = App();
            result = accessibility::Attach(env, args[2], token, screenX, screenY,
                app.accessibilityConfigure, app.accessibilityInvoke);
        }
    }
    napi_value value;
    napi_create_int32(env, result, &value);
    return value;
}

napi_value Touch(napi_env env, napi_callback_info info)
{
    size_t count = 5;
    napi_value args[5];
    uint64_t surfaceId;
    int64_t contact;
    int32_t phase;
    double x, y;
    if (napi_get_cb_info(env, info, &count, args, nullptr, nullptr) != napi_ok || count != 5 ||
        !ParseSurfaceId(env, args[0], surfaceId) ||
        napi_get_value_int64(env, args[1], &contact) != napi_ok ||
        napi_get_value_int32(env, args[2], &phase) != napi_ok || phase < 0 || phase > 3 ||
        napi_get_value_double(env, args[3], &x) != napi_ok || !std::isfinite(x) ||
        napi_get_value_double(env, args[4], &y) != napi_ok || !std::isfinite(y))
        return Error(env, "Invalid XComponent touch event.");
    const auto token = SurfaceToken(surfaceId);
    if (token != 0 && App().ready())
        App().touch(token, contact, phase, x, y);
    return nullptr;
}

napi_value Pointer(napi_env env, napi_callback_info info)
{
    size_t count = 8;
    napi_value args[8];
    uint64_t surfaceId;
    int32_t phase, button, modifiers;
    double x, y, wheelX, wheelY;
    if (napi_get_cb_info(env, info, &count, args, nullptr, nullptr) != napi_ok || count != 8 ||
        !ParseSurfaceId(env, args[0], surfaceId) ||
        napi_get_value_int32(env, args[1], &phase) != napi_ok || phase < 0 || phase > 4 ||
        napi_get_value_int32(env, args[2], &button) != napi_ok || button < 0 || button > 5 ||
        napi_get_value_double(env, args[3], &x) != napi_ok || !std::isfinite(x) ||
        napi_get_value_double(env, args[4], &y) != napi_ok || !std::isfinite(y) ||
        napi_get_value_double(env, args[5], &wheelX) != napi_ok || !std::isfinite(wheelX) ||
        napi_get_value_double(env, args[6], &wheelY) != napi_ok || !std::isfinite(wheelY) ||
        napi_get_value_int32(env, args[7], &modifiers) != napi_ok)
        return Error(env, "Invalid XComponent hardware pointer event.");

    const auto token = SurfaceToken(surfaceId);
    if (token != 0 && App().pointer != nullptr)
        App().pointer(token, phase, button, x, y, wheelX, wheelY, modifiers);
    return nullptr;
}

napi_value Key(napi_env env, napi_callback_info info)
{
    size_t count = 6;
    napi_value args[6];
    uint64_t surfaceId;
    bool down;
    int32_t code, modifiers, unicode, locks;
    if (napi_get_cb_info(env, info, &count, args, nullptr, nullptr) != napi_ok || count != 6 ||
        !ParseSurfaceId(env, args[0], surfaceId) ||
        napi_get_value_bool(env, args[1], &down) != napi_ok ||
        napi_get_value_int32(env, args[2], &code) != napi_ok ||
        napi_get_value_int32(env, args[3], &modifiers) != napi_ok ||
        napi_get_value_int32(env, args[4], &unicode) != napi_ok ||
        napi_get_value_int32(env, args[5], &locks) != napi_ok)
        return Error(env, "Invalid XComponent hardware key event.");

    const auto token = SurfaceToken(surfaceId);
    const bool accepted = token != 0 && App().key != nullptr &&
        App().key(token, down ? 1 : 0, code, modifiers, unicode, locks) != 0;
    napi_value result = nullptr;
    if (napi_get_boolean(env, accepted, &result) != napi_ok)
        return Error(env, "Could not return the XComponent hardware key result.");
    return result;
}

napi_value InputBlur(napi_env env, napi_callback_info info)
{
    size_t count = 1;
    napi_value argument;
    uint64_t surfaceId;
    if (napi_get_cb_info(env, info, &count, &argument, nullptr, nullptr) != napi_ok ||
        count != 1 || !ParseSurfaceId(env, argument, surfaceId))
        return Error(env, "Invalid XComponent surface ID.");

    const auto token = SurfaceToken(surfaceId);
    if (token != 0 && App().inputBlur != nullptr)
        App().inputBlur(token);
    return nullptr;
}

bool WaitAndCloseFence(int fence)
{
    if (fence < 0)
        return true;
    pollfd event{fence, POLLIN, 0};
    const int result = poll(&event, 1, 100);
    close(fence);
    return result > 0 && (event.revents & POLLIN) != 0;
}

bool CopyPixels(OHNativeWindowBuffer* windowBuffer, const char* pixels, int width, int height, int rowBytes)
{
    OH_NativeBuffer* buffer = nullptr;
    if (OH_NativeBuffer_FromNativeWindowBuffer(windowBuffer, &buffer) != 0 || buffer == nullptr)
        return false;
    OH_NativeBuffer_Config geometry{};
    OH_NativeBuffer_GetConfig(buffer, &geometry);
    if (geometry.format != NATIVEBUFFER_PIXEL_FMT_RGBA_8888 || geometry.width < width ||
        geometry.height < height || geometry.stride < width * 4)
        return false;
    void* mapping = nullptr;
    if (OH_NativeBuffer_Map(buffer, &mapping) != 0 || mapping == nullptr)
        return false;
    auto* output = static_cast<char*>(mapping);
    for (int row = 0; row < height; ++row)
        std::memcpy(output + static_cast<size_t>(row) * geometry.stride,
            pixels + static_cast<size_t>(row) * rowBytes, static_cast<size_t>(width) * 4);
    return OH_NativeBuffer_Unmap(buffer) == 0;
}

napi_value RequestPaste(napi_env, napi_callback_info)
{
    if (auto paste = App().paste; paste != nullptr)
        paste();
    return nullptr;
}

napi_value Init(napi_env env, napi_value exports)
{
    const napi_property_descriptor properties[] = {
        {"configureFiles", nullptr, ConfigureFiles, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"configureInputMethod", nullptr, ConfigureInputMethod, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"registerSystemRequests", nullptr, AvaloniaOpenHarmony::RegisterSystemRequests, nullptr, nullptr, nullptr,
            napi_default, nullptr},
        {"unregisterSystemRequests", nullptr, AvaloniaOpenHarmony::UnregisterSystemRequests, nullptr, nullptr, nullptr,
            napi_default, nullptr},
        {"completeSystemRequest", nullptr, AvaloniaOpenHarmony::CompleteSystemRequest, nullptr, nullptr, nullptr,
            napi_default, nullptr},
        {"setLifecycle", nullptr, SetLifecycle, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"setTheme", nullptr, SetTheme, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"refreshDisplays", nullptr, RefreshDisplays, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"setWindowPosition", nullptr, SetWindowPosition, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"setSurfaceOffset", nullptr, SetSurfaceOffset, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"setAvoidArea", nullptr, SetAvoidArea, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"setWindowInsetProperties", nullptr, SetWindowInsetProperties, nullptr, nullptr, nullptr,
            napi_default, nullptr},
        {"clearWindowInsets", nullptr, ClearWindowInsets, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"requestBack", nullptr, RequestBack, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"requestPaste", nullptr, RequestPaste, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"updateSurface", nullptr, UpdateSurface, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"closeSurface", nullptr, CloseSurface, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"configureAccessibility", nullptr, ConfigureAccessibility, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"touch", nullptr, Touch, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"pointer", nullptr, Pointer, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"key", nullptr, Key, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"inputBlur", nullptr, InputBlur, nullptr, nullptr, nullptr, napi_default, nullptr}
    };
    if (napi_define_properties(env, exports, sizeof(properties) / sizeof(properties[0]), properties) != napi_ok)
        return Error(env, "Could not export the Avalonia OpenHarmony bridge.");
    return exports;
}

napi_module module = {1, 0, nullptr, Init, "avalonia_ohos_bridge", nullptr, {nullptr}};

void WarnFrame(const char* stage, int status)
{
    if (!surface.warned) {
        surface.warned = true;
        OH_LOG_Print(LOG_APP, LOG_WARN, LogDomain, Tag, "Frame %{public}s failed (%{public}d)", stage, status);
    }
}
}

extern "C" __attribute__((constructor)) void RegisterAvaloniaOpenHarmony()
{
    napi_module_register(&module);
}

extern "C" OHNativeWindow* avaoh_acquire_egl_window(int64_t token)
{
    std::lock_guard lock(surfaceMutex);
    if (!surface.window || surface.token != token ||
        OH_NativeWindow_NativeObjectReference(surface.window) != 0)
        return nullptr;
    const int status = OH_NativeWindow_NativeWindowHandleOpt(surface.window, SET_USAGE,
        static_cast<uint64_t>(NATIVEBUFFER_USAGE_HW_RENDER | NATIVEBUFFER_USAGE_HW_TEXTURE | NATIVEBUFFER_USAGE_MEM_DMA));
    if (status != 0) {
        OH_NativeWindow_NativeObjectUnreference(surface.window);
        return nullptr;
    }
    surface.eglUsage = true;
    ++surface.eglReferences;
    return surface.window;
}

extern "C" void avaoh_release_egl_window(OHNativeWindow* window)
{
    std::lock_guard lock(surfaceMutex);
    if (surface.window == window && surface.eglReferences != 0)
        --surface.eglReferences;
    OH_NativeWindow_NativeObjectUnreference(window);
}

extern "C" void avaoh_present(int64_t token, const char* pixels, int32_t width, int32_t height, int32_t rowBytes)
{
    if (!pixels || width <= 0 || width > INT32_MAX / 4 || height <= 0 || rowBytes < width * 4)
        return;
    std::lock_guard lock(surfaceMutex);
    if (surface.window == nullptr || surface.token != token || surface.width != width || surface.height != height ||
        surface.eglReferences != 0)
        return;
    if (surface.eglUsage) {
        const int status = OH_NativeWindow_NativeWindowHandleOpt(surface.window, SET_USAGE,
            static_cast<uint64_t>(NATIVEBUFFER_USAGE_CPU_WRITE | NATIVEBUFFER_USAGE_MEM_DMA));
        if (status != 0) {
            WarnFrame("restore CPU usage", status);
            return;
        }
        surface.eglUsage = false;
    }
    OHNativeWindowBuffer* buffer = nullptr;
    int fence = -1;
    int status = OH_NativeWindow_NativeWindowRequestBuffer(surface.window, &buffer, &fence);
    if (status != 0 || buffer == nullptr) {
        if (fence >= 0)
            close(fence);
        WarnFrame("request", status);
        return;
    }
    if (!WaitAndCloseFence(fence) || !CopyPixels(buffer, pixels, width, height, rowBytes)) {
        WarnFrame("map or fence", 0);
        OH_NativeWindow_NativeWindowAbortBuffer(surface.window, buffer);
        return;
    }
    status = OH_NativeWindow_NativeWindowFlushBuffer(surface.window, buffer, -1, Region{nullptr, 0});
    if (status != 0) {
        WarnFrame("flush", status);
        OH_NativeWindow_NativeWindowAbortBuffer(surface.window, buffer);
        return;
    }
    if (!surface.reported) {
        surface.reported = true;
        OH_LOG_Print(LOG_APP, LOG_INFO, LogDomain, Tag, "Presented Avalonia frame %{public}dx%{public}d", width, height);
    }
}

extern "C" void avaoh_report(const char* message)
{
    OH_LOG_Print(LOG_APP, LOG_INFO, LogDomain, Tag, "%{public}s", message);
}

extern "C" int32_t avaoh_ime_window_id()
{
    return inputWindowId.load(std::memory_order_relaxed);
}
