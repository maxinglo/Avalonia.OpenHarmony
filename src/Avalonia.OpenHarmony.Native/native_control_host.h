#pragma once

#include <cstdint>
#include <napi/native_api.h>

namespace native_controls {
bool Bind(napi_env env, napi_value content, int64_t surface);
void Unbind(int64_t surface);
}
