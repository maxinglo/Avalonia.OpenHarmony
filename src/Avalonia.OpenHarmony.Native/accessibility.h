#pragma once

#include <cstdint>
#include <napi/native_api.h>

namespace accessibility {
using Configure = void (*)(int64_t surface, int64_t registration, int32_t enabled);
using Invoke = void (*)(int64_t surface, int64_t registration, int32_t node);

int32_t Attach(napi_env env, napi_value nodeContent, int64_t surface, Configure configure, Invoke invoke);
void Detach(int64_t surface);
}
