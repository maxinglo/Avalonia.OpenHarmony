#include <arkui/native_interface.h>
#include <arkui/native_node.h>
#include <cstdint>
#include <memory>
#include <new>
#include <string>
#include <unordered_map>

namespace {
using Click = void (*)(intptr_t, int32_t);

struct Button {
    Click click;
    intptr_t context;
    int32_t count = 0;
};

ArkUI_NativeNodeAPI_1* api = nullptr;
std::unordered_map<ArkUI_NodeHandle, std::unique_ptr<Button>> buttons;

int32_t SetString(ArkUI_NodeHandle node, ArkUI_NodeAttributeType attribute, const char* value)
{
    const ArkUI_AttributeItem item{nullptr, 0, value};
    return api->setAttribute(node, attribute, &item);
}

void OnClick(ArkUI_NodeEvent* event)
{
    if (OH_ArkUI_NodeEvent_GetEventType(event) != NODE_ON_CLICK)
        return;
    auto* button = static_cast<Button*>(OH_ArkUI_NodeEvent_GetUserData(event));
    if (!button)
        return;
    const auto text = "ArkUI count: " + std::to_string(++button->count);
    SetString(OH_ArkUI_NodeEvent_GetNodeHandle(event), NODE_BUTTON_LABEL, text.c_str());
    button->click(button->context, button->count);
}
}

extern "C" ArkUI_NodeHandle avaoh_counter_create_native_button(Click click, intptr_t context)
{
    OH_ArkUI_GetModuleInterface(ARKUI_NATIVE_NODE, ArkUI_NativeNodeAPI_1, api);
    if (!api)
        return nullptr;
    auto button = std::unique_ptr<Button>(new (std::nothrow) Button{click, context});
    if (!button)
        return nullptr;
    const auto node = api->createNode(ARKUI_NODE_BUTTON);
    if (!node)
        return nullptr;
    ArkUI_NumberValue percent;
    percent.f32 = 1;
    const ArkUI_AttributeItem full{&percent, 1};
    if (SetString(node, NODE_ID, "counter-native-button") != 0 ||
        SetString(node, NODE_BUTTON_LABEL, "ArkUI count: 0") != 0 ||
        api->setAttribute(node, NODE_WIDTH_PERCENT, &full) != 0 ||
        api->setAttribute(node, NODE_HEIGHT_PERCENT, &full) != 0 ||
        api->registerNodeEvent(node, NODE_ON_CLICK, 0, button.get()) != 0 ||
        api->addNodeEventReceiver(node, OnClick) != 0) {
        api->disposeNode(node);
        return nullptr;
    }
    buttons.emplace(node, std::move(button));
    return node;
}

extern "C" void avaoh_counter_dispose_native_button(ArkUI_NodeHandle node)
{
    api->removeNodeEventReceiver(node, OnClick);
    api->unregisterNodeEvent(node, NODE_ON_CLICK);
    api->disposeNode(node);
    buttons.erase(node);
}
