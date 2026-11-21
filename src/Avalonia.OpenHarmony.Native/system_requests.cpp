#include "system_requests.h"

#include <cstdint>
#include <fcntl.h>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>

namespace AvaloniaOpenHarmony {
namespace {
using Completion = void (*)(int64_t, const char*, int32_t);

struct Request {
    int64_t id;
    std::string operation;
    std::string argument;
};

std::mutex requestsMutex;
napi_threadsafe_function dispatcher = nullptr;
std::unordered_map<int64_t, Completion> completions;

void Complete(int64_t id, const char* value, int fd = -1)
{
    Completion complete = nullptr;
    {
        std::lock_guard lock(requestsMutex);
        if (auto entry = completions.extract(id); !entry.empty())
            complete = entry.mapped();
    }
    if (complete != nullptr) {
        const int duplicate = fd < 0 ? -1 : fcntl(fd, F_DUPFD_CLOEXEC, 3);
        complete(id, fd >= 0 && duplicate < 0 ? nullptr : value, duplicate);
    }
}

void DisposeDispatcher(void* value)
{
    auto handle = static_cast<napi_threadsafe_function>(value);
    std::unordered_map<int64_t, Completion> cancelled;
    {
        std::lock_guard lock(requestsMutex);
        if (dispatcher != handle)
            return;
        dispatcher = nullptr;
        completions.swap(cancelled);
    }
    if (handle != nullptr)
        napi_release_threadsafe_function(handle, napi_tsfn_abort);
    for (const auto& [id, complete] : cancelled)
        complete(id, nullptr, -1);
}

void Dispatch(napi_env env, napi_value handler, void*, void* data)
{
    std::unique_ptr<Request> request(static_cast<Request*>(data));
    if (env == nullptr || handler == nullptr) {
        Complete(request->id, nullptr);
        return;
    }
    {
        std::lock_guard lock(requestsMutex);
        if (completions.find(request->id) == completions.end())
            return;
    }

    napi_value receiver;
    napi_value args[3];
    if (napi_get_undefined(env, &receiver) != napi_ok ||
        napi_create_bigint_int64(env, request->id, &args[0]) != napi_ok ||
        napi_create_string_utf8(env, request->operation.c_str(), request->operation.size(), &args[1]) != napi_ok ||
        napi_create_string_utf8(env, request->argument.c_str(), request->argument.size(), &args[2]) != napi_ok ||
        napi_call_function(env, receiver, handler, 3, args, nullptr) != napi_ok)
        Complete(request->id, nullptr);
}

napi_value Error(napi_env env, const char* message)
{
    napi_throw_error(env, nullptr, message);
    return nullptr;
}
}

napi_value RegisterSystemRequests(napi_env env, napi_callback_info info)
{
    size_t count = 1;
    napi_value handler;
    napi_valuetype type;
    if (napi_get_cb_info(env, info, &count, &handler, nullptr, nullptr) != napi_ok || count != 1 ||
        napi_typeof(env, handler, &type) != napi_ok || type != napi_function)
        return Error(env, "A system request handler is required.");

    std::lock_guard lock(requestsMutex);
    if (dispatcher != nullptr)
        return Error(env, "The system request handler is already registered.");

    napi_value name;
    napi_threadsafe_function handle = nullptr;
    if (napi_create_string_utf8(env, "Avalonia system requests", NAPI_AUTO_LENGTH, &name) != napi_ok ||
        napi_create_threadsafe_function(env, handler, nullptr, name, 0, 1, nullptr, nullptr, nullptr,
            Dispatch, &handle) != napi_ok)
        return Error(env, "Could not create the system request handler.");
    if (napi_unref_threadsafe_function(env, handle) != napi_ok ||
        napi_add_env_cleanup_hook(env, DisposeDispatcher, handle) != napi_ok) {
        napi_release_threadsafe_function(handle, napi_tsfn_abort);
        return Error(env, "Could not register the system request handler.");
    }
    dispatcher = handle;
    return nullptr;
}

napi_value UnregisterSystemRequests(napi_env env, napi_callback_info)
{
    napi_threadsafe_function handle;
    {
        std::lock_guard lock(requestsMutex);
        handle = dispatcher;
    }
    if (handle != nullptr && napi_remove_env_cleanup_hook(env, DisposeDispatcher, handle) == napi_ok)
        DisposeDispatcher(handle);
    return nullptr;
}

napi_value CompleteSystemRequest(napi_env env, napi_callback_info info)
{
    size_t count = 3;
    napi_value args[3];
    napi_valuetype type;
    int64_t id;
    bool lossless;
    if (napi_get_cb_info(env, info, &count, args, nullptr, nullptr) != napi_ok || count < 2 ||
        napi_get_value_bigint_int64(env, args[0], &id, &lossless) != napi_ok || !lossless ||
        napi_typeof(env, args[1], &type) != napi_ok)
        return Error(env, "A system request ID and result are required.");

    int32_t fd = -1;
    napi_valuetype fileType = napi_undefined;
    if (count == 3 && (napi_typeof(env, args[2], &fileType) != napi_ok ||
        (fileType != napi_undefined && (napi_get_value_int32(env, args[2], &fd) != napi_ok || fd < 0)))) {
        Complete(id, nullptr);
        return Error(env, "A valid file descriptor is required.");
    }
    if (type != napi_string) {
        Complete(id, nullptr, fd);
        return nullptr;
    }

    try {
        size_t size;
        if (napi_get_value_string_utf8(env, args[1], nullptr, 0, &size) == napi_ok) {
            std::string result(size, '\0');
            if (napi_get_value_string_utf8(env, args[1], result.data(), result.size() + 1, &size) == napi_ok) {
                Complete(id, result.c_str(), fd);
                return nullptr;
            }
        }
    } catch (...) {
        // The managed request must complete even when the native result cannot be copied.
    }
    Complete(id, nullptr);
    return nullptr;
}
}

extern "C" int32_t avaoh_system_request(int64_t id, const char* operation, const char* argument,
    AvaloniaOpenHarmony::Completion complete)
{
    using namespace AvaloniaOpenHarmony;
    if (operation == nullptr || argument == nullptr || complete == nullptr)
        return 0;
    try {
        auto request = std::make_unique<Request>(Request{id, operation, argument});
        std::lock_guard lock(requestsMutex);
        if (dispatcher == nullptr || !completions.emplace(id, complete).second)
            return 0;
        if (napi_acquire_threadsafe_function(dispatcher) != napi_ok) {
            completions.erase(id);
            return 0;
        }
        const auto status = napi_call_threadsafe_function(dispatcher, request.get(), napi_tsfn_nonblocking);
        napi_release_threadsafe_function(dispatcher, napi_tsfn_release);
        if (status != napi_ok) {
            completions.erase(id);
            return 0;
        }
        request.release();
        return 1;
    } catch (...) {
        return 0;
    }
}
