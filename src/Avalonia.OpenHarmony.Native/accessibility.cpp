#include "accessibility.h"

#include <arkui/native_interface.h>
#include <arkui/native_interface_accessibility.h>
#include <arkui/native_node.h>
#include <arkui/native_node_napi.h>
#include <algorithm>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace {
constexpr int32_t Root = 1;
constexpr int32_t RootParent = -2100000;
constexpr int32_t MaximumNodes = 4096;
constexpr size_t MaximumNameBytes = 32768;
constexpr uint32_t Enabled = 1, Focusable = 2, InputFocused = 4, Invokable = 8;
constexpr auto Ok = ARKUI_ACCESSIBILITY_NATIVE_RESULT_SUCCESSFUL;
constexpr auto Missing = ARKUI_ACCESSIBILITY_NATIVE_RESULT_FAILED;
constexpr auto Invalid = ARKUI_ACCESSIBILITY_NATIVE_RESULT_BAD_PARAMETER;
constexpr auto NoMemory = ARKUI_ACCESSIBILITY_NATIVE_RESULT_OUT_OF_MEMORY;
constexpr auto Click = ARKUI_ACCESSIBILITY_NATIVE_ACTION_TYPE_CLICK;
constexpr auto GainFocus = ARKUI_ACCESSIBILITY_NATIVE_ACTION_TYPE_GAIN_ACCESSIBILITY_FOCUS;
constexpr auto ClearFocus = ARKUI_ACCESSIBILITY_NATIVE_ACTION_TYPE_CLEAR_ACCESSIBILITY_FOCUS;

struct NativeNode {
    int32_t id, parent;
    uint32_t state;
    int32_t left, top, right, bottom;
    const char* role;
    const char* name;
};
static_assert(sizeof(NativeNode) == 48 && offsetof(NativeNode, role) == 32,
    "Accessibility ABI must match the C# blittable node.");

struct Node {
    int32_t id, parent;
    uint32_t state;
    ArkUI_AccessibleRect bounds;
    std::string role, name;
    std::vector<int64_t> children;
};

struct Tree {
    std::vector<int32_t> order;
    std::unordered_map<int32_t, Node> nodes;
};

struct Registration {
    int64_t surface, generation;
    std::string instance;
    ArkUI_NativeNodeAPI_1* nodeApi;
    ArkUI_NodeContentHandle content;
    ArkUI_NodeHandle overlay;
    accessibility::Configure configure;
    accessibility::Invoke invoke;
    ArkUI_IntOffset origin;
    std::shared_ptr<const Tree> tree;
    int32_t focus = 0;
};

std::mutex StateLock;
std::unique_ptr<Registration> Current;
int64_t NextRegistration = 0;

struct Query {
    std::shared_ptr<const Tree> tree;
    ArkUI_IntOffset origin{};
    int32_t focus = 0;
};

Query Read(const char* instance)
{
    std::lock_guard lock(StateLock);
    if (!instance || !Current || Current->instance != instance)
        return {};
    return {Current->tree, Current->origin, Current->focus};
}

const Node* Find(const Tree& tree, int64_t id)
{
    if (id == -1) id = Root;
    if (id <= 0 || id > INT32_MAX)
        return nullptr;
    const auto found = tree.nodes.find(static_cast<int32_t>(id));
    return found == tree.nodes.end() ? nullptr : &found->second;
}

bool Actionable(const Node& node) { return (node.state & (Enabled | Invokable)) == (Enabled | Invokable); }
bool Navigable(const Node& node) { return node.id != Root && (!node.name.empty() || (node.state & Focusable) != 0); }

int32_t AddOffset(int32_t value, int32_t offset)
{
    return static_cast<int32_t>(std::clamp<int64_t>(static_cast<int64_t>(value) + offset, INT32_MIN, INT32_MAX));
}

int32_t Fill(const Query& query, const Node& node, ArkUI_AccessibilityElementInfo* info)
{
    if (!info) return NoMemory;
    const auto& bounds = node.bounds;
    ArkUI_AccessibleRect screen{ AddOffset(bounds.leftTopX, query.origin.x),
        AddOffset(bounds.leftTopY, query.origin.y), AddOffset(bounds.rightBottomX, query.origin.x),
        AddOffset(bounds.rightBottomY, query.origin.y) };
    OH_ArkUI_AccessibilityElementInfoSetElementId(info, node.id);
    OH_ArkUI_AccessibilityElementInfoSetParentId(info, node.id == Root ? RootParent : node.parent);
    OH_ArkUI_AccessibilityElementInfoSetComponentType(info, node.role.c_str());
    OH_ArkUI_AccessibilityElementInfoSetAccessibilityText(info, node.name.c_str());
    OH_ArkUI_AccessibilityElementInfoSetScreenRect(info, &screen);
    OH_ArkUI_AccessibilityElementInfoSetVisible(info, true);
    OH_ArkUI_AccessibilityElementInfoSetEnabled(info, (node.state & Enabled) != 0);
    OH_ArkUI_AccessibilityElementInfoSetFocusable(info, Navigable(node));
    OH_ArkUI_AccessibilityElementInfoSetFocused(info, (node.state & InputFocused) != 0);
    OH_ArkUI_AccessibilityElementInfoSetAccessibilityFocused(info, node.id == query.focus);
    OH_ArkUI_AccessibilityElementInfoSetAccessibilityLevel(info, Navigable(node) ? "yes" : "no");
    if (!node.children.empty()) {
        auto ids = node.children;
        OH_ArkUI_AccessibilityElementInfoSetChildNodeIds(info, static_cast<int32_t>(ids.size()), ids.data());
    }
    ArkUI_AccessibleAction actions[]{{GainFocus, ""}, {ClearFocus, ""}, {Click, ""}};
    if (Navigable(node)) {
        OH_ArkUI_AccessibilityElementInfoSetClickable(info, Actionable(node));
        OH_ArkUI_AccessibilityElementInfoSetOperationActions(info, Actionable(node) ? 3 : 2, actions);
    }
    return Ok;
}

int32_t FindById(const char* instance, int64_t id, ArkUI_AccessibilitySearchMode mode, int32_t,
    ArkUI_AccessibilityElementInfoList* list)
{
    const auto query = Read(instance);
    const auto* node = query.tree ? Find(*query.tree, id) : nullptr;
    if (!list || !node) return Missing;

    std::unordered_set<int32_t> added;
    const auto append = [&](const Node& element) {
        return added.insert(element.id).second
            ? Fill(query, element, OH_ArkUI_AddAndGetAccessibilityElementInfo(list)) : Ok;
    };
    if (append(*node) != Ok) return NoMemory;
    const auto& tree = *query.tree;
    if (mode & ARKUI_ACCESSIBILITY_NATIVE_SEARCH_MODE_PREFETCH_PREDECESSORS) {
        for (auto* parent = Find(tree, node->parent); parent; parent = Find(tree, parent->parent))
            if (append(*parent) != Ok) return NoMemory;
    }
    if (mode & ARKUI_ACCESSIBILITY_NATIVE_SEARCH_MODE_PREFETCH_SIBLINGS) {
        if (const auto* parent = Find(tree, node->parent))
            for (const auto child : parent->children)
                if (append(*Find(tree, child)) != Ok) return NoMemory;
    }
    const bool recursive = (mode & ARKUI_ACCESSIBILITY_NATIVE_SEARCH_MODE_PREFETCH_RECURSIVE_CHILDREN) != 0;
    if (recursive || (mode & ARKUI_ACCESSIBILITY_NATIVE_SEARCH_MODE_PREFETCH_CHILDREN)) {
        auto children = node->children;
        for (size_t index = 0; index < children.size(); index++) {
            const auto* child = Find(tree, children[index]);
            if (append(*child) != Ok) return NoMemory;
            if (recursive) children.insert(children.end(), child->children.begin(), child->children.end());
        }
    }
    return Ok;
}

int32_t FindByText(const char* instance, int64_t id, const char* text, int32_t,
    ArkUI_AccessibilityElementInfoList* list)
{
    const auto query = Read(instance);
    const auto* root = query.tree ? Find(*query.tree, id) : nullptr;
    if (!list || !root || !text || !*text) return Invalid;
    std::vector<int64_t> search{root->id};
    for (size_t index = 0; index < search.size(); index++) {
        const auto* element = Find(*query.tree, search[index]);
        if (element->name.find(text) != std::string::npos &&
            Fill(query, *element, OH_ArkUI_AddAndGetAccessibilityElementInfo(list)) != Ok)
            return NoMemory;
        search.insert(search.end(), element->children.begin(), element->children.end());
    }
    return Ok;
}

int32_t FindFocused(const char* instance, int64_t, ArkUI_AccessibilityFocusType type, int32_t,
    ArkUI_AccessibilityElementInfo* info)
{
    const auto query = Read(instance);
    if (!query.tree) return Missing;
    const auto& tree = *query.tree;
    if (type == ARKUI_ACCESSIBILITY_NATIVE_FOCUS_TYPE_ACCESSIBILITY) {
        if (const auto* node = Find(tree, query.focus)) return Fill(query, *node, info);
    } else if (type == ARKUI_ACCESSIBILITY_NATIVE_FOCUS_TYPE_INPUT) {
        for (const auto id : tree.order) {
            const auto& node = tree.nodes.at(id);
            if (node.state & InputFocused) return Fill(query, node, info);
        }
    }
    return Missing;
}

int32_t NextFocus(const char* instance, int64_t id, ArkUI_AccessibilityFocusMoveDirection direction, int32_t,
    ArkUI_AccessibilityElementInfo* info)
{
    const auto query = Read(instance);
    if (!query.tree) return Missing;
    const auto& tree = *query.tree;
    const auto* node = Find(tree, id);
    if (!node) return Missing;
    if (direction == ARKUI_ACCESSIBILITY_NATIVE_DIRECTION_FORWARD) {
        auto next = std::find(tree.order.begin(), tree.order.end(), node->id) + 1;
        for (; next != tree.order.end(); ++next)
            if (const auto& candidate = tree.nodes.at(*next); Navigable(candidate)) return Fill(query, candidate, info);
    } else if (direction == ARKUI_ACCESSIBILITY_NATIVE_DIRECTION_BACKWARD) {
        auto next = node->id == Root ? tree.order.rbegin() :
            std::find(tree.order.rbegin(), tree.order.rend(), node->id) + 1;
        for (; next != tree.order.rend(); ++next)
            if (const auto& candidate = tree.nodes.at(*next); Navigable(candidate)) return Fill(query, candidate, info);
    }
    return Missing;
}

int32_t Execute(const char* instance, int64_t id, ArkUI_Accessibility_ActionType action,
    ArkUI_AccessibilityActionArguments*, int32_t)
{
    std::lock_guard lock(StateLock);
    if (!instance || !Current || Current->instance != instance || !Current->tree) return Missing;
    const auto* node = Find(*Current->tree, id);
    if (!node || !Navigable(*node)) return Missing;
    if (action == GainFocus || action == ClearFocus) {
        if (action == GainFocus) Current->focus = node->id;
        else if (Current->focus == node->id) Current->focus = 0;
        return Ok;
    }
    if (action != Click || !Actionable(*node)) return Missing;
    // This callback queues work on the Avalonia dispatcher and does not call back into this bridge.
    Current->invoke(Current->surface, Current->generation, node->id);
    return Ok;
}

int32_t ClearFocused(const char* instance)
{
    std::lock_guard lock(StateLock);
    if (!instance || !Current || Current->instance != instance) return Missing;
    Current->focus = 0;
    return Ok;
}

int32_t CursorPosition(const char*, int64_t, int32_t, int32_t*) { return Missing; }

ArkUI_AccessibilityProviderCallbacksWithInstance Callbacks{
    FindById, FindByText, FindFocused, NextFocus, Execute, ClearFocused, CursorPosition
};

bool ConfigureOverlay(ArkUI_NativeNodeAPI_1* api, ArkUI_NodeHandle node)
{
    ArkUI_NumberValue extent{};
    extent.f32 = 1.0f;
    const ArkUI_AttributeItem fullSize{&extent, 1, nullptr, nullptr};
    ArkUI_NumberValue mode{};
    mode.i32 = ARKUI_HIT_TEST_MODE_NONE;
    const ArkUI_AttributeItem hitTest{&mode, 1, nullptr, nullptr};
    return api->setAttribute(node, NODE_WIDTH_PERCENT, &fullSize) == 0 &&
        api->setAttribute(node, NODE_HEIGHT_PERCENT, &fullSize) == 0 &&
        api->setAttribute(node, NODE_HIT_TEST_BEHAVIOR, &hitTest) == 0;
}
}

int32_t accessibility::Attach(napi_env env, napi_value nodeContent, int64_t surface, int32_t screenX, int32_t screenY,
    Configure configure, Invoke invoke)
{
    if (!configure || !invoke)
        return Invalid;
    const ArkUI_IntOffset origin{screenX, screenY};
    {
        std::lock_guard lock(StateLock);
        if (Current && Current->surface == surface) {
            Current->origin = origin;
            return Ok;
        }
    }

    ArkUI_NodeContentHandle content = nullptr;
    if (OH_ArkUI_GetNodeContentFromNapiValue(env, nodeContent, &content) != 0 || !content)
        return Invalid;
    ArkUI_NativeNodeAPI_1* api = nullptr;
    OH_ArkUI_GetModuleInterface(ARKUI_NATIVE_NODE, ArkUI_NativeNodeAPI_1, api);
    if (!api) return Missing;
    auto node = api->createNode(ARKUI_NODE_CUSTOM);
    if (!node) return NoMemory;
    if (!ConfigureOverlay(api, node) || OH_ArkUI_NodeContent_AddNode(content, node) != 0) {
        api->disposeNode(node);
        return Missing;
    }
    const int64_t generation = ++NextRegistration;
    auto registration = std::make_unique<Registration>(Registration{
        surface, generation, "avalonia-" + std::to_string(generation), api, content, node, configure, invoke, origin,
        nullptr});
    ArkUI_AccessibilityProvider* provider = nullptr;
    auto result = OH_ArkUI_NativeModule_GetNativeAccessibilityProvider(&node, &provider);
    if (result == Ok)
        result = provider ? OH_ArkUI_AccessibilityProviderRegisterCallbackWithInstance(
            registration->instance.c_str(), provider, &Callbacks) : Missing;
    if (result != Ok) {
        OH_ArkUI_NodeContent_RemoveNode(content, node);
        api->disposeNode(node);
        return result;
    }
    {
        std::lock_guard lock(StateLock);
        Current = std::move(registration);
    }
    configure(surface, generation, 1);
    return Ok;
}

void accessibility::Detach(int64_t surface)
{
    std::unique_ptr<Registration> previous;
    {
        std::lock_guard lock(StateLock);
        if (Current && Current->surface == surface) previous = std::move(Current);
    }
    if (previous) {
        previous->configure(surface, previous->generation, 0);
        OH_ArkUI_NodeContent_RemoveNode(previous->content, previous->overlay);
        previous->nodeApi->disposeNode(previous->overlay);
    }
}

extern "C" void avaoh_submit_accessibility(int64_t surface, int64_t generation, const NativeNode* nodes, int32_t count)
{
    if (count < 0 || count > MaximumNodes || (count > 0 && !nodes)) return;
    try {
        {
            std::lock_guard lock(StateLock);
            if (!Current || Current->surface != surface || Current->generation != generation) return;
        }
        auto tree = std::make_shared<Tree>();
        tree->order.reserve(count);
        tree->nodes.reserve(count);
        for (int32_t index = 0; index < count; index++) {
            const auto& source = nodes[index];
            if (index == 0 ? source.id != Root || source.parent != 0 : source.id <= Root || source.parent <= 0)
                return;
            if (!source.role || !source.name || strnlen(source.role, 65) > 64 ||
                strnlen(source.name, MaximumNameBytes + 1) > MaximumNameBytes ||
                source.left >= source.right || source.top >= source.bottom)
                return;
            if (index > 0) {
                const auto parent = tree->nodes.find(source.parent);
                if (parent == tree->nodes.end()) return;
                parent->second.children.push_back(source.id);
            }
            const auto inserted = tree->nodes.emplace(source.id, Node{source.id, source.parent, source.state,
                {source.left, source.top, source.right, source.bottom}, source.role, source.name, {}});
            if (!inserted.second) return;
            tree->order.push_back(source.id);
        }
        std::lock_guard lock(StateLock);
        if (!Current || Current->surface != surface || Current->generation != generation) return;
        if (tree->nodes.find(Current->focus) == tree->nodes.end()) Current->focus = 0;
        Current->tree = std::move(tree);
    } catch (...) {
        // Keep the previous complete snapshot when a replacement cannot be built.
    }
}
