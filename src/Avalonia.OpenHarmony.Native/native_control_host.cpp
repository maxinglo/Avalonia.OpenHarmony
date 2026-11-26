#include "native_control_host.h"

#include <arkui/native_interface.h>
#include <arkui/native_node.h>
#include <arkui/native_node_napi.h>
#include <memory>
#include <mutex>
#include <new>
#include <unordered_map>
#include <unordered_set>

namespace {
using Work = void (*)(intptr_t, int32_t);

struct DispatchRequest {
    Work work;
    intptr_t data;
};

struct Slot {
    napi_ref reference;
    ArkUI_NodeContentHandle content;
    std::unordered_set<ArkUI_NodeHandle> roots;
};

struct Container {
    ArkUI_NodeHandle root;
    ArkUI_NodeHandle parent;
    ArkUI_NodeHandle child = nullptr;
    int64_t surface = 0;
};

std::mutex dispatcherMutex;
napi_threadsafe_function dispatcher = nullptr;
napi_env dispatcherEnvironment = nullptr;
std::unordered_set<int64_t> supportedSurfaces;
std::unordered_map<int64_t, Slot> slots;
ArkUI_NativeNodeAPI_1* api = nullptr;

void Unmount(Slot& slot)
{
    for (auto root : slot.roots)
        OH_ArkUI_NodeContent_RemoveNode(slot.content, root);
    napi_delete_reference(dispatcherEnvironment, slot.reference);
}

void ReleaseDispatcher(void* data)
{
    auto handle = static_cast<napi_threadsafe_function>(data);
    {
        std::lock_guard lock(dispatcherMutex);
        supportedSurfaces.clear();
        dispatcher = nullptr;
    }
    for (auto& [surface, slot] : slots)
        Unmount(slot);
    slots.clear();
    dispatcherEnvironment = nullptr;
    napi_release_threadsafe_function(handle, napi_tsfn_abort);
}

void Dispatch(napi_env env, napi_value, void*, void* data)
{
    std::unique_ptr<DispatchRequest> request(static_cast<DispatchRequest*>(data));
    request->work(request->data, env != nullptr);
}

bool EnsureDispatcher(napi_env env)
{
    if (dispatcher != nullptr)
        return dispatcherEnvironment == env;

    OH_ArkUI_GetModuleInterface(ARKUI_NATIVE_NODE, ArkUI_NativeNodeAPI_1, api);
    if (api == nullptr)
        return false;

    napi_value name;
    napi_threadsafe_function handle = nullptr;
    if (napi_create_string_utf8(env, "Avalonia native controls", NAPI_AUTO_LENGTH, &name) != napi_ok ||
        napi_create_threadsafe_function(env, nullptr, nullptr, name, 0, 1, nullptr, nullptr, nullptr,
            Dispatch, &handle) != napi_ok)
        return false;
    if (napi_unref_threadsafe_function(env, handle) != napi_ok ||
        napi_add_env_cleanup_hook(env, ReleaseDispatcher, handle) != napi_ok) {
        napi_release_threadsafe_function(handle, napi_tsfn_abort);
        return false;
    }
    dispatcher = handle;
    dispatcherEnvironment = env;
    return true;
}

int32_t SetInt(ArkUI_NodeHandle node, ArkUI_NodeAttributeType attribute, int32_t value)
{
    ArkUI_NumberValue number;
    number.i32 = value;
    const ArkUI_AttributeItem item{&number, 1};
    return api->setAttribute(node, attribute, &item);
}

bool SetRect(ArkUI_NodeHandle node, float x, float y, float width, float height)
{
    ArkUI_NumberValue bounds[4];
    bounds[0].f32 = x;
    bounds[1].f32 = y;
    bounds[2].f32 = width;
    bounds[3].f32 = height;
    const ArkUI_AttributeItem position{bounds, 2};
    const ArkUI_AttributeItem widthItem{bounds + 2, 1};
    const ArkUI_AttributeItem heightItem{bounds + 3, 1};
    return api->setAttribute(node, NODE_POSITION, &position) == 0 &&
        api->setAttribute(node, NODE_WIDTH, &widthItem) == 0 &&
        api->setAttribute(node, NODE_HEIGHT, &heightItem) == 0;
}
}

bool native_controls::Bind(napi_env env, napi_value content, int64_t surface)
{
    if (slots.find(surface) != slots.end())
        return true;

    ArkUI_NodeContentHandle handle = nullptr;
    napi_ref reference = nullptr;
    if (OH_ArkUI_GetNodeContentFromNapiValue(env, content, &handle) != 0 || handle == nullptr ||
        napi_create_reference(env, content, 1, &reference) != napi_ok)
        return false;
    {
        std::lock_guard lock(dispatcherMutex);
        if (!EnsureDispatcher(env)) {
            napi_delete_reference(env, reference);
            return false;
        }
        supportedSurfaces.insert(surface);
    }
    slots.emplace(surface, Slot{reference, handle, {}});
    return true;
}

void native_controls::Unbind(int64_t surface)
{
    {
        std::lock_guard lock(dispatcherMutex);
        supportedSurfaces.erase(surface);
    }
    if (auto slot = slots.extract(surface); !slot.empty())
        Unmount(slot.mapped());
}

extern "C" int32_t avaoh_native_controls_supported(int64_t surface)
{
    std::lock_guard lock(dispatcherMutex);
    return surface == 0 ? dispatcher != nullptr : supportedSurfaces.find(surface) != supportedSurfaces.end();
}

extern "C" int32_t avaoh_native_controls_dispatch(Work work, intptr_t data)
{
    std::unique_ptr<DispatchRequest> request(new (std::nothrow) DispatchRequest{work, data});
    if (!request)
        return 0;
    std::lock_guard lock(dispatcherMutex);
    if (dispatcher == nullptr || napi_call_threadsafe_function(dispatcher, request.get(), napi_tsfn_nonblocking) != napi_ok)
        return 0;
    request.release();
    return 1;
}

extern "C" ArkUI_NodeHandle avaoh_native_control_create_empty()
{
    return api->createNode(ARKUI_NODE_STACK);
}

extern "C" void avaoh_native_control_dispose_node(ArkUI_NodeHandle node)
{
    api->disposeNode(node);
}

extern "C" Container* avaoh_native_control_create_container(ArkUI_NodeHandle* parent)
{
    const auto outer = api->createNode(ARKUI_NODE_STACK);
    const auto inner = api->createNode(ARKUI_NODE_STACK);
    if (outer && inner &&
        SetInt(outer, NODE_CLIP, 1) == 0 &&
        SetInt(outer, NODE_VISIBILITY, ARKUI_VISIBILITY_HIDDEN) == 0 &&
        SetInt(outer, NODE_HIT_TEST_BEHAVIOR, ARKUI_HIT_TEST_MODE_BLOCK_HIERARCHY) == 0 &&
        SetInt(inner, NODE_HIT_TEST_BEHAVIOR, ARKUI_HIT_TEST_MODE_NONE) == 0 &&
        SetInt(inner, NODE_STACK_ALIGN_CONTENT, ARKUI_ALIGNMENT_TOP_START) == 0) {
        auto container = std::unique_ptr<Container>(new (std::nothrow) Container{outer, inner});
        if (container && api->addChild(outer, inner) == 0) {
            *parent = inner;
            return container.release();
        }
    }
    if (inner)
        api->disposeNode(inner);
    if (outer)
        api->disposeNode(outer);
    return nullptr;
}

extern "C" int32_t avaoh_native_control_set_child(Container* container, ArkUI_NodeHandle child)
{
    if (container->child || !child || api->addChild(container->parent, child) != 0)
        return 0;
    container->child = child;
    return 1;
}

extern "C" int32_t avaoh_native_control_mount(Container* container, int64_t surface)
{
    if (container->surface == surface)
        return surface == 0 || slots.find(surface) != slots.end();
    if (auto previous = slots.find(container->surface); previous != slots.end()) {
        if (OH_ArkUI_NodeContent_RemoveNode(previous->second.content, container->root) != 0)
            return 0;
        previous->second.roots.erase(container->root);
    }
    container->surface = 0;
    if (surface == 0)
        return 1;
    const auto target = slots.find(surface);
    if (target == slots.end() || OH_ArkUI_NodeContent_AddNode(target->second.content, container->root) != 0)
        return 0;
    target->second.roots.insert(container->root);
    container->surface = surface;
    return 1;
}

extern "C" int32_t avaoh_native_control_set_bounds(Container* container, float x, float y, float width, float height,
    float clipX, float clipY, float clipWidth, float clipHeight, int32_t visible, int32_t enabled)
{
    const bool show = visible && clipWidth > 0 && clipHeight > 0;
    const bool geometry = SetRect(container->parent, show ? x - clipX : 0, show ? y - clipY : 0, width, height) &&
        (!show || SetRect(container->root, clipX, clipY, clipWidth, clipHeight));
    const bool state = SetInt(container->root, NODE_ENABLED, enabled) == 0 &&
        SetInt(container->root, NODE_VISIBILITY, show ? ARKUI_VISIBILITY_VISIBLE : ARKUI_VISIBILITY_HIDDEN) == 0;
    return geometry && state;
}

extern "C" void avaoh_native_control_dispose_container(Container* container)
{
    // Remove the child first so that the caller can reuse or dispose it separately.
    if (container->child)
        api->removeChild(container->parent, container->child);
    avaoh_native_control_mount(container, 0);
    api->removeChild(container->root, container->parent);
    api->disposeNode(container->parent);
    api->disposeNode(container->root);
    delete container;
}
