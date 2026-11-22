#pragma once

#include <napi/native_api.h>

namespace AvaloniaOpenHarmony {
napi_value RegisterSystemRequests(napi_env env, napi_callback_info info);
napi_value UnregisterSystemRequests(napi_env env, napi_callback_info info);
napi_value CompleteSystemRequest(napi_env env, napi_callback_info info);
}
