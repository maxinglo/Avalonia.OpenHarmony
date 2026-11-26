#pragma once

#include <cstdint>
#include <limits>
#include <mutex>
#include <vector>
#include <window_manager/oh_display_manager.h>

namespace avalonia {

// Keep this in sync with OpenHarmonyHost.NativeDisplay.
struct DisplaySnapshot {
    int64_t id;
    const char* name;
    double scaling;
    int32_t left;
    int32_t top;
    int32_t width;
    int32_t height;
    int32_t workLeft;
    int32_t workTop;
    int32_t workWidth;
    int32_t workHeight;
    int32_t orientation;
    int32_t primary;
};
static_assert(sizeof(DisplaySnapshot) == 64);

using DisplayCallback = void (*)(const DisplaySnapshot*, int32_t);

inline int32_t ScreenOrientation(NativeDisplayManager_Orientation orientation)
{
    switch (orientation) {
    case DISPLAY_MANAGER_PORTRAIT:
        return 2;
    case DISPLAY_MANAGER_LANDSCAPE:
        return 1;
    case DISPLAY_MANAGER_PORTRAIT_INVERTED:
        return 8;
    case DISPLAY_MANAGER_LANDSCAPE_INVERTED:
        return 4;
    default:
        return 0;
    }
}

inline void ReadAvailableArea(DisplaySnapshot& snapshot)
{
    NativeDisplayManager_Rect* area = nullptr;
    if (OH_NativeDisplayManager_CreateAvailableArea(static_cast<uint64_t>(snapshot.id), &area) != DISPLAY_MANAGER_OK
        || area == nullptr)
        return;
    // The available area is measured relative to the queried display.
    const int64_t left = static_cast<int64_t>(snapshot.left) + area->left;
    const int64_t top = static_cast<int64_t>(snapshot.top) + area->top;
    if (area->left >= 0 && area->top >= 0 && area->width > 0 && area->height > 0
        && static_cast<int64_t>(area->left) + area->width <= snapshot.width
        && static_cast<int64_t>(area->top) + area->height <= snapshot.height
        && left <= std::numeric_limits<int32_t>::max() && top <= std::numeric_limits<int32_t>::max()) {
        snapshot.workLeft = static_cast<int32_t>(left);
        snapshot.workTop = static_cast<int32_t>(top);
        snapshot.workWidth = static_cast<int32_t>(area->width);
        snapshot.workHeight = static_cast<int32_t>(area->height);
    }
    OH_NativeDisplayManager_DestroyAvailableArea(area);
}

inline void PublishDisplays(DisplayCallback callback)
{
    NativeDisplayManager_DisplaysInfo* displays = nullptr;
    if (OH_NativeDisplayManager_CreateAllDisplays(&displays) != DISPLAY_MANAGER_OK || displays == nullptr)
        return;

    uint64_t primaryId = std::numeric_limits<uint64_t>::max();
    NativeDisplayManager_DisplayInfo* primary = nullptr;
    if (OH_NativeDisplayManager_CreatePrimaryDisplay(&primary) == DISPLAY_MANAGER_OK && primary != nullptr) {
        primaryId = primary->id;
        OH_NativeDisplayManager_DestroyDisplay(primary);
    } else {
        OH_NativeDisplayManager_GetDefaultDisplayId(&primaryId);
    }

    std::vector<DisplaySnapshot> snapshots;
    const auto limit = displays->displaysLength < 256 ? displays->displaysLength : 256;
    if (displays->displaysInfo != nullptr) {
        snapshots.reserve(limit);
        for (uint32_t index = 0; index < limit; ++index) {
            const auto& display = displays->displaysInfo[index];
            const bool isPrimary = display.id == primaryId;
            if (!display.isAlive || display.width <= 0 || display.height <= 0 || !(display.densityPixels > 0))
                continue;

            int32_t left = 0;
            int32_t top = 0;
            if (OH_NativeDisplayManager_GetDisplayPosition(display.id, &left, &top) != DISPLAY_MANAGER_OK) {
                if (!isPrimary)
                    continue;
                left = top = 0;
            }

            DisplaySnapshot snapshot{display.id, display.name, display.densityPixels, left, top,
                display.width, display.height, left, top, display.width, display.height,
                ScreenOrientation(display.orientation), isPrimary ? 1 : 0};
            ReadAvailableArea(snapshot);
            snapshots.push_back(snapshot);
        }
    }
    // The managed callback copies the names before the SDK's snapshot is released.
    callback(snapshots.data(), static_cast<int32_t>(snapshots.size()));
    OH_NativeDisplayManager_DestroyAllDisplays(displays);
}

// The system callback can arrive off the ArkUI thread. Serialize snapshots and
// wait for an in-flight callback before detaching the managed callback.
inline std::mutex displayPublishMutex;
inline DisplayCallback displayObserver = nullptr;
inline uint32_t availableAreaListener = 0;
inline bool availableAreaListenerRegistered = false;

inline void PublishDisplaysSynchronized(DisplayCallback callback)
{
    std::lock_guard<std::mutex> lock(displayPublishMutex);
    if (callback != nullptr)
        PublishDisplays(callback);
}

inline void PublishObservedDisplays()
{
    std::lock_guard<std::mutex> lock(displayPublishMutex);
    if (displayObserver != nullptr)
        PublishDisplays(displayObserver);
}

inline void OnAvailableAreaChanged(uint64_t)
{
    PublishObservedDisplays();
}

// Start and stop are called on the ArkUI thread.
inline bool StartAvailableAreaObservation(DisplayCallback callback)
{
    if (callback == nullptr)
        return false;
    {
        std::lock_guard<std::mutex> lock(displayPublishMutex);
        displayObserver = callback;
    }
    if (!availableAreaListenerRegistered) {
        availableAreaListenerRegistered = OH_NativeDisplayManager_RegisterAvailableAreaChangeListener(
            OnAvailableAreaChanged, &availableAreaListener) == DISPLAY_MANAGER_OK;
    }
    if (!availableAreaListenerRegistered) {
        std::lock_guard<std::mutex> lock(displayPublishMutex);
        displayObserver = nullptr;
    }
    return availableAreaListenerRegistered;
}

inline void StopAvailableAreaObservation()
{
    if (availableAreaListenerRegistered &&
        OH_NativeDisplayManager_UnregisterAvailableAreaChangeListener(availableAreaListener) == DISPLAY_MANAGER_OK)
        availableAreaListenerRegistered = false;
    std::lock_guard<std::mutex> lock(displayPublishMutex);
    displayObserver = nullptr;
}

} // namespace avalonia
