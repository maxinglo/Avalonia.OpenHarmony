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
    }

    // Keep the NativeAOT library loaded for the process lifetime.
    void* handle = nullptr;
    void (*resize)(int64_t, int32_t, int32_t, double) = nullptr;
    void (*destroy)(int64_t) = nullptr;
    void (*touch)(int64_t, int64_t, int32_t, double, double) = nullptr;

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
    if (oldToken != 0)
        app.destroy(oldToken);
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
    if (token != 0 && App().ready())
        App().destroy(token);
    return nullptr;
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
    int64_t token = 0;
    {
        std::lock_guard lock(surfaceMutex);
        if (surface.id == surfaceId)
            token = surface.token;
    }
    if (token != 0 && App().ready())
        App().touch(token, contact, phase, x, y);
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

napi_value Init(napi_env env, napi_value exports)
{
    const napi_property_descriptor properties[] = {
        {"configureFiles", nullptr, ConfigureFiles, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"configureInputMethod", nullptr, ConfigureInputMethod, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"updateSurface", nullptr, UpdateSurface, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"closeSurface", nullptr, CloseSurface, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"touch", nullptr, Touch, nullptr, nullptr, nullptr, napi_default, nullptr}
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

extern "C" void avaoh_present(int64_t token, const char* pixels, int32_t width, int32_t height, int32_t rowBytes)
{
    if (!pixels || width <= 0 || width > INT32_MAX / 4 || height <= 0 || rowBytes < width * 4)
        return;
    std::lock_guard lock(surfaceMutex);
    if (surface.window == nullptr || surface.token != token || surface.width != width || surface.height != height)
        return;
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
