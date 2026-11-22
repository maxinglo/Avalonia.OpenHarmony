cmake_minimum_required(VERSION 3.16)
project(AvaloniaOpenHarmony LANGUAGES CXX)

set(AVALONIA_OHOS_APP_LIBRARY "libavalonia_ohos_app.so" CACHE STRING "NativeAOT application library")
option(AVALONIA_OHOS_TRACE_ACCESSIBILITY_SCROLL "Trace accessibility scroll requests and offsets" OFF)
add_library(avalonia_ohos_bridge SHARED bridge.cpp system_requests.cpp accessibility.cpp)
target_compile_features(avalonia_ohos_bridge PRIVATE cxx_std_17)
target_compile_definitions(avalonia_ohos_bridge PRIVATE AVALONIA_OHOS_APP_LIBRARY="${AVALONIA_OHOS_APP_LIBRARY}")
if(AVALONIA_OHOS_TRACE_ACCESSIBILITY_SCROLL)
    target_compile_definitions(avalonia_ohos_bridge PRIVATE AVALONIA_OHOS_TRACE_ACCESSIBILITY_SCROLL)
endif()
target_link_libraries(avalonia_ohos_bridge PRIVATE libace_napi.z.so libace_ndk.z.so libhilog_ndk.z.so libnative_buffer.so
        libnative_window.so libnative_display_manager.so libdl.so)
